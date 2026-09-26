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
    IReadOnlyList<NetWorthLine> Lines);

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
            lines.Add(a.IsLiability
                ? new NetWorthLine("liability", a.Name, Math.Max(0, -eur))
                : new NetWorthLine("cash", a.Name, eur));
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
        var investments = lines.Where(l => l.Group == "investments").Sum(l => l.Value);
        var manual = lines.Where(l => l.Group == "manual").Sum(l => l.Value);
        var liabilities = lines.Where(l => l.Group == "liability").Sum(l => l.Value);
        var totalAssets = cash + investments + manual;
        return new NetWorthBreakdown(today, totalAssets, liabilities, totalAssets - liabilities, cash, investments,
            manual, lines);
    }

    public async Task<NetWorthHistory> HistoryAsync(CancellationToken ct)
    {
        var current = await ComputeAsync(ct);
        var snapshots = await db.NetWorthSnapshots.AsNoTracking().OrderBy(s => s.Date).ToListAsync(ct);
        var series = snapshots.Where(s => s.Date != current.Date)
            .Select(s => new NetWorthPoint(s.Date, s.AssetsBase, s.LiabilitiesBase, s.NetWorthBase))
            .Append(new NetWorthPoint(current.Date, current.Assets, current.Liabilities, current.NetWorth))
            .ToList();
        var first = series[0];
        var change = series.Count > 1 ? current.NetWorth - first.NetWorth : (decimal?)null;
        return new NetWorthHistory(current, change,
            change is not null && first.NetWorth != 0 ? decimal.Round(change.Value / Math.Abs(first.NetWorth), 6) : null,
            series.Count > 1 ? first.Date : null, series);
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
