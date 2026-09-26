using Investments.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Investments.Application.Fx;

/// <summary>
/// EUR conversion factors from cached ECB reference rates. The network is used only on a cache miss, and
/// each history window is fetched at most once per process lifetime.
/// </summary>
public sealed class FxRates(IInvestmentsDb db, IFxRateSource source, TimeProvider clock, ILogger<FxRates> logger)
{
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static readonly HashSet<(FxHistory, DateOnly)> Fetched = [];

    /// <summary>EUR value of one unit of <paramref name="currency"/> on (or just before) <paramref name="date"/>.</summary>
    public async Task<decimal?> EurPerUnitAsync(string currency, DateOnly date, CancellationToken ct)
    {
        if (currency == Currency.Base)
        {
            return 1m;
        }

        var cached = await LookupAsync(currency, date, ct);
        if (cached is not null)
        {
            return cached;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var window = today.DayNumber - date.DayNumber <= 5 ? FxHistory.Latest
            : today.DayNumber - date.DayNumber <= 85 ? FxHistory.Last90Days
            : FxHistory.Full;
        await FetchAsync(window, today, ct);
        return await LookupAsync(currency, date, ct);
    }

    public async Task<Dictionary<string, decimal>> EurPerUnitAsync(IEnumerable<string> currencies, DateOnly date,
        CancellationToken ct)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var currency in currencies.Distinct())
        {
            if (await EurPerUnitAsync(currency, date, ct) is { } factor)
            {
                result[currency] = factor;
            }
        }

        return result;
    }

    private async Task<decimal?> LookupAsync(string currency, DateOnly date, CancellationToken ct)
    {
        // Weekends and holidays have no reference rate: use the latest rate within the previous week.
        var floor = date.AddDays(-7);
        var rate = await db.FxRates.AsNoTracking()
            .Where(r => r.Quote == currency && r.Date <= date && r.Date >= floor)
            .OrderByDescending(r => r.Date)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);
        return rate is > 0 ? decimal.Round(1m / rate.Value, 10) : null;
    }

    private async Task FetchAsync(FxHistory window, DateOnly today, CancellationToken ct)
    {
        await FetchLock.WaitAsync(ct);
        try
        {
            if (!Fetched.Add((window, today)))
            {
                return;
            }

            var rates = await source.FetchAsync(window, ct);
            var dates = rates.Select(r => r.Date).Distinct().ToList();
            var existing = (await db.FxRates.Where(r => dates.Contains(r.Date))
                    .Select(r => new { r.Date, r.Quote }).ToListAsync(ct))
                .Select(x => (x.Date, x.Quote)).ToHashSet();
            db.FxRates.AddRange(rates.Where(r => !existing.Contains((r.Date, r.Quote))));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            logger.LogWarning(ex, "FX rate fetch failed for window {Window}", window);
        }
        finally
        {
            FetchLock.Release();
        }
    }
}
