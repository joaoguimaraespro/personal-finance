using Investments.Application.Abstractions;
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
public sealed class PriceHistoryService(IInvestmentsDb db, IPriceHistorySource source, TimeProvider clock,
    ILogger<PriceHistoryService> logger)
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

    private async Task EnsureOneAsync(Guid securityId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var security = await db.Securities.AsNoTracking().FirstOrDefaultAsync(s => s.Id == securityId, ct);
        if (security is null || security.EffectiveAssetClass is AssetClass.Cash)
        {
            return;
        }

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

        if (listing!.Symbol is not { } symbol)
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

            var existing = (await db.MarketPrices.AsNoTracking()
                    .Where(p => p.SecurityId == securityId && p.Date >= rangeFrom && p.Date <= rangeTo)
                    .Select(p => p.Date).ToListAsync(ct))
                .ToHashSet();
            foreach (var close in series.Closes.Where(c => c.Date >= rangeFrom && c.Date <= rangeTo && c.Close > 0)
                         .DistinctBy(c => c.Date))
            {
                if (existing.Add(close.Date))
                {
                    db.MarketPrices.Add(MarketPrice.Create(securityId, close.Date, decimal.Round(close.Close, 8),
                        series.Currency, DataSource.MarketData));
                }
            }

            listing.Cover(rangeFrom, rangeTo);
            await db.SaveChangesAsync(ct);
        }
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
