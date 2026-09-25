using Investments.Application.Abstractions;
using Investments.Application.Fx;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Sync;

/// <summary>
/// The only write path for investment data (Validate → Normalize → Deduplicate → Persist). Every record is keyed
/// by (source, external id) so repeated syncs and overlapping CSV imports never create duplicates.
/// </summary>
public sealed class PortfolioSyncWriter(IInvestmentsDb db, FxRates fx, TimeProvider clock)
{
    public async Task<Guid> ResolveSecurityAsync(SecurityReport report, DataSource source, CancellationToken ct)
    {
        var mapped = await db.BrokerSymbols.AsNoTracking()
            .Where(b => b.Source == source && b.Symbol == report.BrokerSymbol)
            .Select(b => (Guid?)b.SecurityId)
            .FirstOrDefaultAsync(ct);
        if (mapped is { } id)
        {
            return id;
        }

        // Same ISIN at another broker → same security; that is what makes the consolidated view work.
        var isin = report.Isin?.Trim().ToUpperInvariant();
        var security = isin is null
            ? null
            : await db.Securities.FirstOrDefaultAsync(s => s.Isin == isin, ct);
        if (security is null)
        {
            security = Security.Create(isin, report.Symbol, report.Exchange, report.Name, report.Currency,
                report.AssetClass);
            db.Securities.Add(security);
        }

        db.BrokerSymbols.Add(BrokerSymbol.Create(source, report.BrokerSymbol, security.Id));
        await db.SaveChangesAsync(ct);
        return security.Id;
    }

    /// <summary>Positions are a full snapshot: anything not reported any more has been sold.</summary>
    public async Task<SyncCounts> ReplacePositionsAsync(Guid accountId, DataSource source,
        IReadOnlyList<PositionReport> reports, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await db.Positions.Where(p => p.AccountId == accountId).ToListAsync(ct);
        var seen = new HashSet<Guid>();
        int imported = 0, updated = 0;
        foreach (var r in reports.Where(r => r.Quantity != 0))
        {
            var securityId = await ResolveSecurityAsync(r.Security, source, ct);
            if (!seen.Add(securityId))
            {
                continue;
            }

            var position = existing.FirstOrDefault(p => p.SecurityId == securityId);
            if (position is null)
            {
                db.Positions.Add(Position.Report(accountId, securityId, r.Quantity, r.AveragePrice, r.LastPrice,
                    r.PriceAsOf, source, now));
                imported++;
            }
            else
            {
                position.Update(r.Quantity, r.AveragePrice, r.LastPrice, r.PriceAsOf, now);
                updated++;
            }

            await RecordPriceAsync(securityId, DateOnly.FromDateTime(r.PriceAsOf.UtcDateTime), r.LastPrice,
                r.Security.Currency, source, ct);
        }

        var closed = existing.Where(p => !seen.Contains(p.SecurityId)).ToList();
        db.Positions.RemoveRange(closed);
        await db.SaveChangesAsync(ct);
        return new SyncCounts(imported, updated + closed.Count, 0);
    }

    public async Task<SyncCounts> AddTradesAsync(Guid accountId, DataSource source, IReadOnlyList<TradeReport> reports,
        CancellationToken ct)
    {
        var known = await KnownIdsAsync(db.Trades.Where(t => t.Source == source), reports.Select(r => r.ExternalId), ct);
        var imported = 0;
        foreach (var r in reports.Where(r => !known.Contains(r.ExternalId)).DistinctBy(r => r.ExternalId))
        {
            var securityId = await ResolveSecurityAsync(r.Security, source, ct);
            var date = DateOnly.FromDateTime(r.ExecutedAt.UtcDateTime);
            var costsFactor = await fx.EurPerUnitAsync(r.FeesCurrency, date, ct) ?? 0m;
            var accountFactor = await fx.EurPerUnitAsync(r.AccountCurrency, date, ct) ?? 0m;
            var tradeFactor = await fx.EurPerUnitAsync(r.Currency, date, ct) ?? 0m;
            var gross = r.Quantity * r.Price;
            var net = r.NetAmountInAccountCurrency is { } n
                ? n * accountFactor
                : (r.Side == TradeSide.Buy ? -gross : gross) * tradeFactor - (r.Fees + r.Taxes) * costsFactor;
            db.Trades.Add(Trade.Record(accountId, securityId, r.Side, r.Quantity, r.Price, r.Currency, r.Fees, r.Taxes,
                decimal.Round((r.Fees + r.Taxes) * costsFactor, 4), decimal.Round(net, 4),
                r.RealizedPnlInAccountCurrency is { } pnl ? decimal.Round(pnl * accountFactor, 4) : null,
                r.ExecutedAt, source, r.ExternalId));
            imported++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncCounts(imported, 0, reports.Count - imported);
    }

    public async Task<SyncCounts> AddDividendsAsync(Guid accountId, DataSource source,
        IReadOnlyList<DividendReport> reports, CancellationToken ct)
    {
        var ids = reports.Select(r => r.ExternalId).ToList();
        var existing = await db.Dividends.Where(d => d.Source == source && ids.Contains(d.ExternalId)).ToListAsync(ct);
        int imported = 0, updated = 0;
        foreach (var r in reports.DistinctBy(r => r.ExternalId))
        {
            var known = existing.FirstOrDefault(d => d.ExternalId == r.ExternalId);
            var (gross, withholding, derived) = ResolveWithholding(r);
            if (known is not null)
            {
                // A later report with an explicit withholding tax replaces an inferred one.
                if (known.WithholdingDerived && !derived)
                {
                    known.ApplyReportedWithholding(gross, withholding);
                    updated++;
                }

                continue;
            }

            var securityId = await ResolveSecurityAsync(r.Security, source, ct);
            var factor = await fx.EurPerUnitAsync(r.Currency, r.PaidOn, ct) ?? 0m;
            db.Dividends.Add(Dividend.Record(accountId, securityId, r.PaidOn, gross, withholding, r.NetAmount,
                r.Currency, decimal.Round(r.NetAmount * factor, 4), derived, source, r.ExternalId));
            imported++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncCounts(imported, updated, reports.Count - imported - updated);
    }

    public async Task<SyncCounts> AddCashMovementsAsync(Guid accountId, DataSource source,
        IReadOnlyList<CashMovementReport> reports, CancellationToken ct)
    {
        var known = await KnownIdsAsync(db.CashMovements.Where(c => c.Source == source),
            reports.Select(r => r.ExternalId), ct);
        var imported = 0;
        foreach (var r in reports.Where(r => !known.Contains(r.ExternalId)).DistinctBy(r => r.ExternalId))
        {
            var factor = await fx.EurPerUnitAsync(r.Currency, DateOnly.FromDateTime(r.OccurredAt.UtcDateTime), ct) ?? 0m;
            db.CashMovements.Add(CashMovement.Record(accountId, r.Type, r.Amount, r.Currency,
                decimal.Round(r.Amount * factor, 4), r.OccurredAt, Truncate(r.Description), source, r.ExternalId));
            imported++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncCounts(imported, 0, reports.Count - imported);
    }

    public async Task SetCashAsync(Guid accountId, string currency, decimal amount, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cash = await db.CashBalances.FirstOrDefaultAsync(c => c.AccountId == accountId && c.Currency == currency, ct);
        if (cash is null)
        {
            db.CashBalances.Add(CashBalance.Report(accountId, currency, amount, now));
        }
        else
        {
            cash.Update(amount, now);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Broker-reported daily values (e.g. IBKR NAV) take precedence over values computed locally.</summary>
    public async Task<SyncCounts> UpsertSnapshotsAsync(Guid accountId, IReadOnlyList<SnapshotReport> reports,
        string origin, CancellationToken ct)
    {
        var dates = reports.Select(r => r.Date).ToList();
        var existing = await db.PortfolioSnapshots.Where(s => s.AccountId == accountId && dates.Contains(s.Date))
            .ToListAsync(ct);
        int imported = 0, updated = 0;
        foreach (var r in reports.DistinctBy(r => r.Date))
        {
            var factor = await fx.EurPerUnitAsync(r.Currency, r.Date, ct) ?? 0m;
            var value = decimal.Round((r.TotalValue - r.Cash) * factor, 4);
            var cash = decimal.Round(r.Cash * factor, 4);
            var flow = decimal.Round((r.NetFlow ?? 0) * factor, 4);
            var snapshot = existing.FirstOrDefault(s => s.Date == r.Date);
            if (snapshot is null)
            {
                db.PortfolioSnapshots.Add(PortfolioSnapshot.Create(accountId, r.Date, value, cash, flow, origin));
                imported++;
            }
            else if (snapshot.Origin != SnapshotOrigins.Broker || origin == SnapshotOrigins.Broker)
            {
                snapshot.Update(value, cash, flow, origin);
                updated++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new SyncCounts(imported, updated, reports.Count - imported - updated);
    }

    public async Task RecordPriceAsync(Guid securityId, DateOnly date, decimal close, string currency,
        DataSource source, CancellationToken ct)
    {
        var price = await db.MarketPrices.FirstOrDefaultAsync(p => p.SecurityId == securityId && p.Date == date, ct);
        if (price is null)
        {
            db.MarketPrices.Add(MarketPrice.Create(securityId, date, close, currency, source));
        }
        else
        {
            price.Update(close);
        }
    }

    /// <summary>Removes everything imported for an account (used when a connection is deleted with "purge").</summary>
    public async Task<int> PurgeAccountAsync(Guid accountId, CancellationToken ct)
    {
        var removed = 0;
        removed += await db.Positions.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        removed += await db.Trades.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        removed += await db.Dividends.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        removed += await db.CashMovements.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        removed += await db.CashBalances.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        removed += await db.PortfolioSnapshots.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(ct);
        return removed;
    }

    private static (decimal Gross, decimal Withholding, bool Derived) ResolveWithholding(DividendReport r)
    {
        if (r.WithholdingTax is { } wht)
        {
            return (r.GrossAmount ?? r.NetAmount + wht, wht, false);
        }

        if (r.GrossAmount is { } gross)
        {
            return (gross, Math.Max(0, gross - r.NetAmount), false);
        }

        // Trading 212's API reports net amount and gross per share only: infer the tax, flag it as derived.
        if (r.GrossPerShare is { } perShare && r.Quantity is { } qty)
        {
            var inferred = decimal.Round(perShare * qty, 4);
            return (inferred, Math.Max(0, inferred - r.NetAmount), true);
        }

        return (r.NetAmount, 0, true);
    }

    private static async Task<HashSet<string>> KnownIdsAsync<T>(IQueryable<T> query, IEnumerable<string> ids,
        CancellationToken ct) where T : class
    {
        var list = ids.ToList();
        return (await query.Select(x => EF.Property<string>(x, "ExternalId"))
            .Where(id => list.Contains(id)).ToListAsync(ct)).ToHashSet();
    }

    private static string? Truncate(string? text) =>
        text is null ? null : new string(text.Where(c => !char.IsControl(c)).Take(200).ToArray());
}

public static class SnapshotOrigins
{
    public const string Broker = "broker";
    public const string Computed = "computed";
}
