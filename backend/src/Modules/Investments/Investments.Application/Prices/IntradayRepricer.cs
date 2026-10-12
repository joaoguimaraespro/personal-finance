using System.Collections.Concurrent;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Prices;

/// <summary>
/// Interactive Brokers reports end-of-day data (Flex), so its positions would keep yesterday's close all day. Between
/// syncs their price follows the public market price instead (only the listing symbol is sent); quantity and cost
/// still come from IBKR, and the next sync's price replaces it. A public price older than IBKR's is never used.
/// </summary>
public sealed class IntradayRepricer(IInvestmentsDb db, PriceHistoryService prices, TimeProvider clock)
{
    /// <summary>A security the provider has nothing newer for (weekend, closed market) is asked again after this.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    private const int RecentDays = 10;

    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> LastAttempt = new();

    private static readonly DataSource[] EndOfDay = [DataSource.InteractiveBrokers];

    /// <summary>Re-prices positions whose price is older than <paramref name="maxAge"/>; returns how many changed.</summary>
    public async Task<int> RepriceAsync(TimeSpan maxAge, CancellationToken ct)
    {
        if (!prices.Enabled)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var positions = await db.Positions.Where(p => EndOfDay.Contains(p.Source)).ToListAsync(ct);
        var changed = 0;
        foreach (var group in positions.Where(p => p.PriceAsOfUtc < now - maxAge).GroupBy(p => p.SecurityId))
        {
            if (LastAttempt.TryGetValue(group.Key, out var last) && now - last < RetryAfter)
            {
                continue;
            }

            LastAttempt[group.Key] = now;
            if (await prices.RefreshRecentAsync(group.Key, RecentDays, ct) is not { Close: > 0 } latest)
            {
                continue;
            }

            foreach (var p in group.Where(p => latest.Date >= DateOnly.FromDateTime(p.PriceAsOfUtc.UtcDateTime)))
            {
                var asOf = ManualCrypto.PriceAsOf(latest.Date, now);
                if (p.LastPrice != latest.Close || p.PriceAsOfUtc != asOf)
                {
                    p.Reprice(latest.Close, asOf);
                    changed++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }
}
