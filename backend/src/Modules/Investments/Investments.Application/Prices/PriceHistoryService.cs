using Investments.Application.Abstractions;
using Investments.Application.Fx;
using Investments.Application.Portfolio;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Investments.Application.Prices;

/// <summary>
/// Makes sure public daily closes exist in <c>MarketPrices</c> for the days a reconstruction needs. The listing
/// chosen for each security and the range already downloaded are cached in <c>PriceListings</c>, so each day is
/// fetched once. Prices reported by brokers are never overwritten. Failures are logged and skipped — callers
/// fall back to trade prices.
/// </summary>
public sealed class PriceHistoryService(IInvestmentsDb db, IPriceHistorySource source, FxRates fx,
    TimeProvider clock, ILogger<PriceHistoryService> logger)
{
    /// <summary>A listing that was not found is looked up again after this long.</summary>
    private static readonly TimeSpan RetryNotFound = TimeSpan.FromDays(30);

    public bool Enabled => source.Enabled;

    /// <summary>Fetches missing closes for each security over [from, to]; returns how many securities failed.</summary>
    public async Task<int> EnsureAsync(IReadOnlyDictionary<Guid, DateOnly> fromBySecurity, DateOnly to,
        CancellationToken ct)
    {
        if (!source.Enabled || fromBySecurity.Count == 0)
        {
            return 0;
        }

        var failures = 0;
        foreach (var (securityId, from) in fromBySecurity)
        {
            if (from > to)
            {
                continue;
            }

            try
            {
                await EnsureOneAsync(securityId, from, to, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                // Only the security id is logged: no quantities or values are involved here anyway.
                logger.LogWarning(ex, "Price history unavailable for security {SecurityId} from {Provider}",
                    securityId, source.Name);
            }
        }

        return failures;
    }

    /// <summary>
    /// Whether the provider quotes this security (resolving and caching its listing first). Used before a coin
    /// entered by hand is accepted.
    /// </summary>
    public async Task<bool> HasListingAsync(Guid securityId, CancellationToken ct)
    {
        if (!source.Enabled)
        {
            return false;
        }

        var security = await db.Securities.AsNoTracking().FirstAsync(s => s.Id == securityId, ct);
        return (await ResolveListingAsync(security, ct)).Symbol is not null;
    }

    /// <summary>
    /// Re-downloads the last <paramref name="days"/> days of closes, overwriting earlier downloads (never broker
    /// prices), so a security quoted around the clock (crypto) has today's live price and yesterday's final close.
    /// Returns the latest close, or null when the provider has none right now.
    /// </summary>
    public async Task<DailyClose?> RefreshRecentAsync(Guid securityId, int days, CancellationToken ct)
    {
        if (!source.Enabled)
        {
            return null;
        }

        try
        {
            var security = await db.Securities.AsNoTracking().FirstAsync(s => s.Id == securityId, ct);
            var listing = await ResolveListingAsync(security, ct);
            if (listing.Symbol is not { } symbol)
            {
                return null;
            }

            var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var from = to.AddDays(-days);
            var series = await source.GetDailyClosesAsync(symbol, from, to, ct);
            if (series is null || series.Closes.Count == 0)
            {
                return null;
            }

            series = await InSecurityCurrencyAsync(security, series, ct);
            var existing = await db.MarketPrices
                .Where(p => p.SecurityId == securityId && p.Date >= from && p.Date <= to).ToListAsync(ct);
            DailyClose? latest = null;
            foreach (var close in series.Closes.Where(c => c.Date >= from && c.Date <= to && c.Close > 0)
                         .DistinctBy(c => c.Date).OrderBy(c => c.Date))
            {
                var value = decimal.Round(close.Close, 12);
                latest = new DailyClose(close.Date, value);
                var known = existing.FirstOrDefault(p => p.Date == close.Date);
                if (known is null)
                {
                    db.MarketPrices.Add(MarketPrice.Create(securityId, close.Date, value, series.Currency,
                        DataSource.MarketData));
                }
                else if (known.Source == DataSource.MarketData && known.Currency == series.Currency)
                {
                    known.Update(value);
                }
            }

            if (listing.CoveredFrom is { } coveredFrom && listing.CoveredTo >= from.AddDays(-1))
            {
                listing.Cover(coveredFrom, to);
            }

            await db.SaveChangesAsync(ct);
            return latest;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Latest price unavailable for security {SecurityId} from {Provider}", securityId,
                source.Name);
            return null;
        }
    }

    private async Task EnsureOneAsync(Guid securityId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var security = await db.Securities.AsNoTracking().FirstOrDefaultAsync(s => s.Id == securityId, ct);
        if (security is null || security.EffectiveAssetClass is AssetClass.Cash)
        {
            return;
        }

        var listing = await ResolveListingAsync(security, ct);
        if (listing.Symbol is not { } symbol)
        {
            return;
        }

        foreach (var (rangeFrom, rangeTo) in Missing(listing, from, to))
        {
            var series = await source.GetDailyClosesAsync(symbol, rangeFrom, rangeTo, ct);
            if (series is null)
            {
                return; // Provider error: leave the range uncovered so the next run tries again.
            }

            series = await InSecurityCurrencyAsync(security, series, ct);
            var existing = (await db.MarketPrices.AsNoTracking()
                    .Where(p => p.SecurityId == securityId && p.Date >= rangeFrom && p.Date <= rangeTo)
                    .Select(p => p.Date).ToListAsync(ct))
                .ToHashSet();
            foreach (var close in series.Closes.Where(c => c.Date >= rangeFrom && c.Date <= rangeTo && c.Close > 0)
                         .DistinctBy(c => c.Date))
            {
                if (existing.Add(close.Date))
                {
                    db.MarketPrices.Add(MarketPrice.Create(securityId, close.Date, decimal.Round(close.Close, 12),
                        series.Currency, DataSource.MarketData));
                }
            }

            listing.Cover(rangeFrom, rangeTo);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<PriceListing> ResolveListingAsync(Security security, CancellationToken ct)
    {
        var securityId = security.Id;
        var listing = await db.PriceListings.FirstOrDefaultAsync(l => l.SecurityId == securityId, ct);
        var now = clock.GetUtcNow();
        var stale = listing is null || listing.Provider != source.Name ||
                    (listing.Symbol is null && now - listing.ResolvedAtUtc > RetryNotFound);
        if (stale)
        {
            var brokerSymbols = await db.BrokerSymbols.AsNoTracking().Where(b => b.SecurityId == securityId)
                .Select(b => new { b.Source, b.Symbol }).ToListAsync(ct);
            var query = new ListingQuery(security.Isin, security.Symbol, security.Currency,
                ListingHints.For(security, brokerSymbols.Select(b => (b.Source, b.Symbol))));
            var match = await source.ResolveAsync(query, ct);
            if (listing is null)
            {
                listing = PriceListing.Create(securityId, source.Name, match?.Symbol, match?.Currency, now);
                db.PriceListings.Add(listing);
            }
            else
            {
                listing.Resolve(source.Name, match?.Symbol, match?.Currency, now);
            }

            await db.SaveChangesAsync(ct);
        }

        return listing!;
    }

    /// <summary>
    /// Coins quoted only in USD are stored in the coin's own currency (EUR) at that day's reference rate, so the
    /// gain and today's change of a coin entered in EUR are in EUR. Other securities keep the listing's currency
    /// (converted when valued).
    /// </summary>
    private async Task<PriceSeries> InSecurityCurrencyAsync(Security security, PriceSeries series,
        CancellationToken ct)
    {
        if (security.EffectiveAssetClass != AssetClass.Crypto || series.Currency == security.Currency ||
            series.Closes.Count == 0)
        {
            return series;
        }

        var from = series.Closes.Min(c => c.Date);
        var to = series.Closes.Max(c => c.Date);
        var currencies = new[] { series.Currency, security.Currency }.Where(c => c != Currency.Base).ToList();
        foreach (var currency in currencies)
        {
            await fx.EurPerUnitAsync(currency, from, ct); // Loads ECB history into the cache when missing.
            await fx.EurPerUnitAsync(currency, to, ct);
        }

        var floor = from.AddDays(-10);
        var rows = await db.FxRates.AsNoTracking()
            .Where(r => currencies.Contains(r.Quote) && r.Date >= floor && r.Date <= to && r.Rate > 0)
            .Select(r => new { r.Quote, r.Date, r.Rate })
            .ToListAsync(ct);
        var table = new FxTable(rows.GroupBy(r => r.Quote).ToDictionary(g => g.Key,
            g => g.OrderBy(r => r.Date).Select(r => (r.Date, 1m / r.Rate)).ToArray()));
        var converted = new List<DailyClose>();
        foreach (var close in series.Closes)
        {
            if (table.EurPerUnit(series.Currency, close.Date) is { } fromFactor &&
                table.EurPerUnit(security.Currency, close.Date) is { } toFactor && toFactor > 0)
            {
                converted.Add(close with { Close = close.Close * fromFactor / toFactor });
            }
        }

        return new PriceSeries(security.Currency, converted);
    }

    /// <summary>
    /// The parts of [from, to] not yet downloaded, extended to touch the covered range so coverage stays one
    /// contiguous interval.
    /// </summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> Missing(PriceListing listing, DateOnly from, DateOnly to)
    {
        if (listing.CoveredFrom is not { } coveredFrom || listing.CoveredTo is not { } coveredTo)
        {
            return [(from, to)];
        }

        var ranges = new List<(DateOnly, DateOnly)>();
        if (from < coveredFrom)
        {
            ranges.Add((from, coveredFrom.AddDays(-1))); // Contiguous with the covered range: no gaps.
        }

        if (to > coveredTo)
        {
            ranges.Add((coveredTo.AddDays(1), to));
        }

        return ranges;
    }
}
