using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Domain.Accounts;
using Investments.Application.Abstractions;
using Investments.Application.Fx;
using Investments.Application.Portfolio;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Investments.Application.NetWorth;

public sealed record NetWorthLine(string Group, string Name, decimal Value);

public sealed record NetWorthBreakdown(
    DateOnly Date,
    decimal Assets,
    decimal Liabilities,
    decimal NetWorth,
    decimal Cash,
    decimal Investments,
    decimal ManualAssets,
    IReadOnlyList<NetWorthLine> Lines,
    decimal Restricted = 0,
    decimal Receivable = 0);

public sealed record NetWorthHistory(NetWorthBreakdown Current, decimal? ChangeSinceStart,
    decimal? ChangeSinceStartPercent, DateOnly? StartDate, IReadOnlyList<NetWorthPoint> Series);

public sealed record NetWorthPoint(DateOnly Date, decimal Assets, decimal Liabilities, decimal NetWorth);

/// <summary>Assets − liabilities, across bank accounts, broker accounts and manually valued items.</summary>
public sealed class NetWorthService(IInvestmentsDb db, IFinanceDb finance, PortfolioQueries portfolio, FxRates fx,
    TimeProvider clock)
{
    public async Task<NetWorthBreakdown> ComputeAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var lines = new List<NetWorthLine>();

        // Bank/cash/card/loan accounts: balances from the ledger. Broker accounts are valued from positions.
        var accounts = await finance.Accounts.AsNoTracking()
            .Where(a => a.ArchivedAtUtc == null && a.Kind != AccountKind.Broker).ToListAsync(ct);
        var balances = await AccountBalances.ComputeAsync(finance, accounts, today, ct);
        var factors = await fx.EurPerUnitAsync(accounts.Select(a => a.Currency), today, ct);
        foreach (var a in accounts)
        {
            var eur = decimal.Round(balances[a.Id] * factors.GetValueOrDefault(a.Currency, 0), 2);
            // Restricted money (a meal card) is an asset, shown apart from money that can pay anything. People owe the
            // owner (receivable) or are owed by the owner (a liability) what their shared costs left open.
            if (a.IsPerson)
            {
                lines.Add(eur >= 0
                    ? new NetWorthLine("receivable", a.Name, eur)
                    : new NetWorthLine("liability", a.Name, -eur));
                continue;
            }

            lines.Add(a.IsLiability
                ? new NetWorthLine("liability", a.Name, Math.Max(0, -eur))
                : new NetWorthLine(a.RestrictedUse ? "restricted" : "cash", a.Name, eur));
        }

        var summary = await portfolio.SummaryAsync(new PortfolioScope(), ct);
        lines.AddRange(summary.Accounts.Select(a => new NetWorthLine("investments", a.Name, a.MarketValue + a.Cash)));

        var assets = await db.ManualAssets.AsNoTracking().Include(m => m.Valuations)
            .Where(m => m.ArchivedAtUtc == null).ToListAsync(ct);
        var assetFactors = await fx.EurPerUnitAsync(assets.Select(a => a.Currency), today, ct);
        foreach (var m in assets)
        {
            var value = decimal.Round((m.ValueOn(today) ?? 0) * assetFactors.GetValueOrDefault(m.Currency, 0), 2);
            lines.Add(new NetWorthLine(m.IsLiability ? "liability" : "manual", m.Name, value));
        }

        var cash = lines.Where(l => l.Group == "cash").Sum(l => l.Value);
        var restricted = lines.Where(l => l.Group == "restricted").Sum(l => l.Value);
        var investments = lines.Where(l => l.Group == "investments").Sum(l => l.Value);
        var manual = lines.Where(l => l.Group == "manual").Sum(l => l.Value);
        var receivable = lines.Where(l => l.Group == "receivable").Sum(l => l.Value);
        var liabilities = lines.Where(l => l.Group == "liability").Sum(l => l.Value);
        var totalAssets = cash + restricted + investments + manual + receivable;
        return new NetWorthBreakdown(today, totalAssets, liabilities, totalAssets - liabilities, cash, investments,
            manual, lines, restricted, receivable);
    }

    public async Task<NetWorthHistory> HistoryAsync(CancellationToken ct)
    {
        var current = await ComputeAsync(ct);
        var snapshots = await db.NetWorthSnapshots.AsNoTracking().OrderBy(s => s.Date).ToListAsync(ct);
        var recorded = snapshots.Where(s => s.Date != current.Date)
            .Select(s => new NetWorthPoint(s.Date, s.AssetsBase, s.LiabilitiesBase, s.NetWorthBase))
            .ToList();
        var firstRecorded = recorded.Count > 0 ? recorded[0].Date : current.Date;
        var series = (await ReconstructedAsync(firstRecorded, ct)).Concat(recorded)
            .Append(new NetWorthPoint(current.Date, current.Assets, current.Liabilities, current.NetWorth))
            .ToList();
        var first = series[0];
        var change = series.Count > 1 ? current.NetWorth - first.NetWorth : (decimal?)null;
        // A percentage from an almost empty start (the first deposit) says nothing: only the amount is shown then.
        var meaningful = change is not null && first.NetWorth != 0 &&
                         Math.Abs(first.NetWorth) >= Math.Abs(current.NetWorth) * 0.1m;
        return new NetWorthHistory(current, change,
            meaningful ? decimal.Round(change!.Value / Math.Abs(first.NetWorth), 6) : null,
            series.Count > 1 ? first.Date : null, series);
    }

    /// <summary>At most this far back, one point per month end.</summary>
    private const int MaxReconstructedMonths = 120;

    /// <summary>
    /// Net worth before daily snapshots began (a new install, or after starting fresh), one point per month end: ledger
    /// balances on that day, each broker account at its last valuation on or before it (from its own history or the
    /// reconstructed one) and hand-entered assets at their value then. Computed on the fly, so later edits show.
    /// Foreign-currency balances use today's rate.
    /// </summary>
    private async Task<List<NetWorthPoint>> ReconstructedAsync(DateOnly before, CancellationToken ct)
    {
        var valuations = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => s.Date < before)
            .Select(s => new { s.AccountId, s.Date, Value = s.MarketValueBase + s.CashBase })
            .ToListAsync(ct);
        var accounts = await finance.Accounts.AsNoTracking()
            .Where(a => a.ArchivedAtUtc == null && a.Kind != AccountKind.Broker).ToListAsync(ct);
        var assets = await db.ManualAssets.AsNoTracking().Include(m => m.Valuations)
            .Where(m => m.ArchivedAtUtc == null).ToListAsync(ct);
        var starts = valuations.Select(v => v.Date)
            .Concat(accounts.Select(a => a.OpeningBalanceOn))
            .Where(d => d < before).ToList();
        if (starts.Count == 0)
        {
            return [];
        }

        var monthEnds = new List<DateOnly>();
        var first = starts.Min();
        var end = new DateOnly(first.Year, first.Month, 1).AddMonths(1).AddDays(-1);
        for (; end < before; end = end.AddDays(1).AddMonths(1).AddDays(-1))
        {
            monthEnds.Add(end);
        }

        monthEnds = monthEnds.TakeLast(MaxReconstructedMonths).ToList();
        if (monthEnds.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var factors = await fx.EurPerUnitAsync(accounts.Select(a => a.Currency).Concat(assets.Select(a => a.Currency)),
            today, ct);
        var balances = await AccountBalances.ComputeOnAsync(finance, accounts, monthEnds, ct);
        var byAccount = valuations.GroupBy(v => v.AccountId)
            .Select(g => g.OrderBy(v => v.Date).Select(v => (v.Date, v.Value)).ToList()).ToList();
        var points = new List<NetWorthPoint>();
        foreach (var day in monthEnds)
        {
            decimal assetsTotal = 0, liabilities = 0;
            foreach (var a in accounts.Where(a => a.OpeningBalanceOn <= day))
            {
                var eur = balances[day][a.Id] * factors.GetValueOrDefault(a.Currency, 0);
                if (a.IsLiability || (a.IsPerson && eur < 0))
                {
                    liabilities += Math.Max(0, -eur);
                }
                else
                {
                    assetsTotal += eur;
                }
            }

            assetsTotal += byAccount.Sum(list => list.LastOrDefault(v => v.Date <= day).Value);
            foreach (var m in assets)
            {
                var value = (m.ValueOn(day) ?? 0) * factors.GetValueOrDefault(m.Currency, 0);
                if (m.IsLiability)
                {
                    liabilities += value;
                }
                else
                {
                    assetsTotal += value;
                }
            }

            assetsTotal = decimal.Round(assetsTotal, 2);
            liabilities = decimal.Round(liabilities, 2);
            if (assetsTotal != 0 || liabilities != 0)
            {
                points.Add(new NetWorthPoint(day, assetsTotal, liabilities, assetsTotal - liabilities));
            }
        }

        return points;
    }

    /// <summary>Daily snapshot so net worth can be charted over time. Idempotent per day.</summary>
    public async Task SnapshotAsync(CancellationToken ct)
    {
        var current = await ComputeAsync(ct);
        var breakdown = JsonSerializer.Serialize(new
        {
            current.Cash,
            current.Investments,
            current.ManualAssets,
            current.Liabilities,
        });
        var existing = await db.NetWorthSnapshots.FirstOrDefaultAsync(s => s.Date == current.Date, ct);
        if (existing is null)
        {
            db.NetWorthSnapshots.Add(NetWorthSnapshot.Create(current.Date, current.Assets, current.Liabilities, breakdown));
        }
        else
        {
            existing.Update(current.Assets, current.Liabilities, breakdown);
        }

        await db.SaveChangesAsync(ct);
    }
}
