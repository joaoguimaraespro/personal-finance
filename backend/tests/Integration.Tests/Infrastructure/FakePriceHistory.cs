using System.Collections.Concurrent;
using Investments.Application.Abstractions;

namespace Integration.Tests.Infrastructure;

/// <summary>Constant weekday closes per ISIN; counts requests so tests can check that each day is fetched once.</summary>
public sealed class FakePriceHistory : IPriceHistorySource
{
    public ConcurrentDictionary<string, decimal> ClosesByIsin { get; } = new()
    {
        ["IE00BK5BQT80"] = 110m,
        ["IE00BDBRDM35"] = 5m,
    };

    public ConcurrentQueue<(string Symbol, DateOnly From, DateOnly To)> Requests { get; } = new();

    public string Name => "fake";

    public bool Enabled => true;

    public Task<ListingMatch?> ResolveAsync(ListingQuery query, CancellationToken ct) =>
        Task.FromResult(query.Isin is { } isin && ClosesByIsin.ContainsKey(isin) ? new ListingMatch(isin, "EUR") : null);

    public Task<PriceSeries?> GetDailyClosesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Requests.Enqueue((symbol, from, to));
        var close = ClosesByIsin[symbol];
        var days = new List<DailyClose>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(new DailyClose(d, close));
            }
        }

        return Task.FromResult<PriceSeries?>(new PriceSeries("EUR", days));
    }
}
