using System.Collections.Concurrent;
using Investments.Application.Abstractions;

namespace Integration.Tests.Infrastructure;

/// <summary>
/// Constant weekday closes per ISIN; counts requests so tests can check that each day is fetched once. Coins
/// trade every day: a constant close, and today's (live) price <see cref="CoinTodayFactor"/> times higher.
/// </summary>
public sealed class FakePriceHistory : IPriceHistorySource
{
    public const decimal CoinTodayFactor = 1.02m;

    /// <summary>A coin's price 24 hours ago, relative to its constant close (the live price is 1.02×).</summary>
    public const decimal Coin24hAgoFactor = 1.01m;

    public ConcurrentDictionary<string, decimal> ClosesByIsin { get; } = new()
    {
        ["IE00BK5BQT80"] = 110m,
        ["IE00BDBRDM35"] = 5m,
    };

    /// <summary>Coin listings by symbol ("BTC-EUR"); a coin missing here has no price.</summary>
    public ConcurrentDictionary<string, decimal> CoinCloses { get; } = new()
    {
        ["BTC-EUR"] = 50_000m,
    };

    public ConcurrentQueue<(string Symbol, DateOnly From, DateOnly To)> Requests { get; } = new();

    public string Name => "fake";

    public bool Enabled => true;

    public Task<ListingMatch?> ResolveAsync(ListingQuery query, CancellationToken ct)
    {
        if (query.ExchangeHints.FirstOrDefault(h => h.Exchange == "CRYPTO") is { Root: { } coin } &&
            CoinCloses.ContainsKey(coin + "-EUR"))
        {
            return Task.FromResult<ListingMatch?>(new ListingMatch(coin + "-EUR", "EUR"));
        }

        return Task.FromResult(query.Isin is { } isin && ClosesByIsin.ContainsKey(isin)
            ? new ListingMatch(isin, "EUR")
            : null);
    }

    public Task<PriceSeries?> GetDailyClosesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Requests.Enqueue((symbol, from, to));
        var days = new List<DailyClose>();
        if (CoinCloses.TryGetValue(symbol, out var coin))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            for (var d = from; d <= to && d <= today; d = d.AddDays(1))
            {
                days.Add(new DailyClose(d, d == today ? coin * CoinTodayFactor : coin));
            }

            return Task.FromResult<PriceSeries?>(new PriceSeries("EUR", days));
        }

        var close = ClosesByIsin[symbol];
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(new DailyClose(d, close));
            }
        }

        return Task.FromResult<PriceSeries?>(new PriceSeries("EUR", days));
    }

    public Task<Rolling24h?> GetRolling24hAsync(string symbol, CancellationToken ct)
    {
        if (!CoinCloses.TryGetValue(symbol, out var coin))
        {
            return Task.FromResult<Rolling24h?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        return Task.FromResult<Rolling24h?>(new Rolling24h("EUR", now, coin * CoinTodayFactor, now.AddHours(-24),
            coin * Coin24hAgoFactor));
    }

    public Task<IReadOnlyList<CoinMatch>> SearchCoinsAsync(string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CoinMatch>>(
            new[] { new CoinMatch("BTC", "BTC", "Bitcoin"), new CoinMatch("NOPRICE", "NOPRICE", "No price coin") }
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            c.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList());
}
