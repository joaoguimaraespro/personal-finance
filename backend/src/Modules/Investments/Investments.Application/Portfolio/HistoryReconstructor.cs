using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Fx;
using Investments.Application.Prices;
using Investments.Application.Sync;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Investments.Application.Portfolio;

/// <summary>
/// Gives accounts whose broker reports no valuation history (Trading 212) a daily history from their first
/// trade or deposit up to the first real snapshot, valued with public closing prices
/// (<see cref="HistoryReconstruction"/>). Reconstructed snapshots are regenerated from scratch on every run, are
/// never written over a <see cref="SnapshotOrigins.Broker"/> or <see cref="SnapshotOrigins.Computed"/> snapshot,
/// and are replaced by real snapshots as they arrive.
/// </summary>
public sealed class HistoryReconstructor(
    IInvestmentsDb db,
    PortfolioQueries portfolio,
    PriceHistoryService prices,
    FxRates fx,
    TimeProvider clock,
    ILogger<HistoryReconstructor> logger)
{
    /// <summary>Older history is not reconstructed (and not fetched).</summary>
    private const int MaxYears = 15;

    public async Task<int> RebuildAllAsync(CancellationToken ct)
    {
        var accounts = await portfolio.BrokerAccountsAsync(new PortfolioScope(), ct);
        var written = 0;
        foreach (var accountId in accounts.Keys)
        {
            written += await RebuildAsync(accountId, ct);
        }

        return written;
    }

    /// <summary>Returns the number of reconstructed days written (0 when the account does not need any).</summary>
    public async Task<int> RebuildAsync(Guid accountId, CancellationToken ct)
    {
        var real = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => s.AccountId == accountId && s.Origin != SnapshotOrigins.Reconstructed)
            .Select(s => new { s.Date, s.Origin, s.CashBase })
            .ToListAsync(ct);
        if (real.Count == 0 || real.Any(s => s.Origin == SnapshotOrigins.Broker))
        {
            return 0; // No anchor yet, or the broker reports its own history (IBKR NAV, demo).
        }

        var anchor = real.MinBy(s => s.Date)!;
        var fills = (await db.Trades.AsNoTracking().Where(t => t.AccountId == accountId)
                .Select(t => new { t.ExecutedAtUtc, t.SecurityId, t.Side, t.Quantity, t.Price, t.Currency, t.BaseAmount })
                .ToListAsync(ct))
            .Select(t => new LedgerFill(DateOnly.FromDateTime(t.ExecutedAtUtc.UtcDateTime), t.SecurityId,
                t.Side == TradeSide.Buy ? t.Quantity : -t.Quantity, t.Price, t.Currency, t.BaseAmount))
            .ToList();
        var movements = (await db.CashMovements.AsNoTracking().Where(c => c.AccountId == accountId)
                .Select(c => new { c.OccurredAtUtc, c.BaseAmount, c.Type })
                .ToListAsync(ct))
            .Select(c => new LedgerCash(DateOnly.FromDateTime(c.OccurredAtUtc.UtcDateTime), c.BaseAmount,
                c.Type is CashMovementType.Deposit or CashMovementType.Withdrawal));
        var dividends = (await db.Dividends.AsNoTracking().Where(d => d.AccountId == accountId)
                .Select(d => new { d.PaidOn, d.NetBaseAmount })
                .ToListAsync(ct))
            .Select(d => new LedgerCash(d.PaidOn, d.NetBaseAmount, false));
        var cash = movements.Concat(dividends).ToList();

        var to = anchor.Date.AddDays(-1);
        var floor = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddYears(-MaxYears);
        var firstEvent = fills.Select(f => f.Date).Concat(cash.Select(c => c.Date)).DefaultIfEmpty(anchor.Date).Min();
        var from = firstEvent < floor ? floor : firstEvent;

        if (from > to)
        {
            await ReplaceAsync(accountId, [], [], ct);
            return 0;
        }

        var quantitiesNow = await db.Positions.AsNoTracking().Where(p => p.AccountId == accountId)
            .ToDictionaryAsync(p => p.SecurityId, p => p.Quantity, ct);
        var securities = fills.Select(f => f.SecurityId).Concat(quantitiesNow.Keys).Distinct().ToList();

        // Public closes for each security from the first day it was (or may have been) held.
        var firstHeld = securities.ToDictionary(id => id, id =>
        {
            var first = fills.Where(f => f.SecurityId == id).Select(f => f.Date).DefaultIfEmpty(from).Min();
            return (first < from ? from : first).AddDays(-HistoryReconstruction.StaleCloseDays);
        });
        await prices.EnsureAsync(firstHeld, to, ct);

        var closeFloor = from.AddDays(-HistoryReconstruction.StaleCloseDays);
        var closes = (await db.MarketPrices.AsNoTracking()
                .Where(p => securities.Contains(p.SecurityId) && p.Date >= closeFloor && p.Date <= to)
                .Select(p => new { p.SecurityId, p.Date, p.Close, p.Currency })
                .ToListAsync(ct))
            .GroupBy(p => p.SecurityId)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<PriceClose>)g.Select(p => new PriceClose(p.Date, p.Close, p.Currency)).ToList());

        var currencies = fills.Select(f => f.Currency)
            .Concat(closes.Values.SelectMany(v => v.Select(c => c.Currency)))
            .Select(c => CurrencyUnits.Normalize(c).Currency)
            .Where(c => c != Currency.Base)
            .Distinct().ToList();
        var rates = await RatesAsync(currencies, from, to, ct);

        var days = HistoryReconstruction.Build(new ReconstructionInput(from, to, quantitiesNow, anchor.Date,
            anchor.CashBase, fills, cash, closes, rates.EurPerUnit));
        var written = await ReplaceAsync(accountId, days, real.Select(s => s.Date).ToHashSet(), ct);
        logger.LogInformation("Reconstructed {Days} days of history for account {AccountId} ({Estimated} partly estimated)",
            written, accountId, days.Count(d => d.EstimatedHoldings > 0));
        return written;
    }

    /// <summary>Swaps the previous reconstruction for the new one in a single save; real days are kept.</summary>
    private async Task<int> ReplaceAsync(Guid accountId, IReadOnlyList<ReconstructedDay> days,
        HashSet<DateOnly> taken, CancellationToken ct)
    {
        var stale = await db.PortfolioSnapshots
            .Where(s => s.AccountId == accountId && s.Origin == SnapshotOrigins.Reconstructed).ToListAsync(ct);
        db.PortfolioSnapshots.RemoveRange(stale);
        var written = 0;
        foreach (var day in days.Where(d => !taken.Contains(d.Date)))
        {
            db.PortfolioSnapshots.Add(PortfolioSnapshot.Reconstructed(accountId, day.Date, day.MarketValue, day.Cash,
                day.NetFlow, SnapshotOrigins.Reconstructed, day.EstimatedHoldings));
            written++;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    private async Task<FxTable> RatesAsync(IReadOnlyList<string> currencies, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        foreach (var currency in currencies)
        {
            await fx.EurPerUnitAsync(currency, from, ct); // Loads ECB history into the cache when missing.
        }

        var floor = from.AddDays(-10);
        var rows = await db.FxRates.AsNoTracking()
            .Where(r => currencies.Contains(r.Quote) && r.Date >= floor && r.Date <= to)
            .Select(r => new { r.Quote, r.Date, r.Rate })
            .ToListAsync(ct);
        return new FxTable(rows.GroupBy(r => r.Quote).ToDictionary(g => g.Key,
            g => g.Where(r => r.Rate > 0).OrderBy(r => r.Date).Select(r => (r.Date, 1m / r.Rate)).ToArray()));
    }
}

/// <summary>Minor-unit quotes (London pence) expressed in their major currency.</summary>
public static class CurrencyUnits
{
    public static (string Currency, decimal Factor) Normalize(string currency) => currency switch
    {
        "GBX" or "GBp" => ("GBP", 0.01m),
        "ZAc" or "ZAC" => ("ZAR", 0.01m),
        "ILA" => ("ILS", 0.01m),
        _ => (currency.ToUpperInvariant(), 1m),
    };
}

/// <summary>In-memory ECB rates: the latest rate on or before a day, else the earliest one after it.</summary>
public sealed class FxTable(IReadOnlyDictionary<string, (DateOnly Date, decimal EurPerUnit)[]> rates)
{
    public decimal? EurPerUnit(string currency, DateOnly date)
    {
        var (major, factor) = CurrencyUnits.Normalize(currency);
        if (major == Currency.Base)
        {
            return factor;
        }

        if (!rates.TryGetValue(major, out var sorted) || sorted.Length == 0)
        {
            return null;
        }

        int lo = 0, hi = sorted.Length - 1, found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid].Date <= date)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return sorted[found].EurPerUnit * factor;
    }
}
