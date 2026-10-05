namespace Investments.Application.Abstractions;

/// <summary>
/// What a price provider may learn about a security to find its listing: identifiers only. Quantities, values
/// and account data never leave the server.
/// </summary>
/// <param name="ExchangeHints">Provider-neutral listing hints derived from broker tickers/exchanges, as
/// (symbol root, exchange code) pairs, e.g. ("VWCE", "XETRA").</param>
public sealed record ListingQuery(string? Isin, string Symbol, string Currency,
    IReadOnlyList<(string Root, string Exchange)> ExchangeHints);

/// <summary>A provider listing, e.g. Yahoo "VWCE.DE" quoted in EUR.</summary>
public sealed record ListingMatch(string Symbol, string Currency);

public readonly record struct DailyClose(DateOnly Date, decimal Close);

/// <summary>A coin offered by the provider's search, e.g. Id "SOL" (Yahoo "SOL-USD"), shown as "SOL · Solana".</summary>
/// <param name="Id">Provider-neutral coin id used to find its listings ("SOL", "PEPE24478").</param>
/// <param name="Symbol">Ticker to show ("PEPE").</param>
public sealed record CoinMatch(string Id, string Symbol, string Name);

/// <summary>
/// The latest quote and the price 24 hours before it (a rolling day, as crypto exchanges show the change), in
/// <see cref="Currency"/>.
/// </summary>
public sealed record Rolling24h(string Currency, DateTimeOffset LatestAtUtc, decimal LatestPrice,
    DateTimeOffset ReferenceAtUtc, decimal ReferencePrice);

/// <summary>Daily closes in <see cref="Currency"/> (minor units such as GBp already converted to GBP).</summary>
public sealed record PriceSeries(string Currency, IReadOnlyList<DailyClose> Closes);

/// <summary>
/// Public end-of-day prices, used to reconstruct the valuation history of brokers whose APIs have none
/// (Trading 212). Implementations must be polite (rate limited; the caller caches results) and may be disabled.
/// </summary>
public interface IPriceHistorySource
{
    /// <summary>Provider id stored with cached listings, e.g. "yahoo".</summary>
    string Name { get; }

    bool Enabled { get; }

    /// <summary>The best listing for the security, or null when the provider has none.</summary>
    Task<ListingMatch?> ResolveAsync(ListingQuery query, CancellationToken ct);

    /// <summary>Closes for [from, to] (inclusive); null or empty when the provider has no data for that range.</summary>
    Task<PriceSeries?> GetDailyClosesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// Coins whose name or ticker matches <paramref name="query"/>, best first. Only the typed text is sent.
    /// Providers without crypto return nothing.
    /// </summary>
    Task<IReadOnlyList<CoinMatch>> SearchCoinsAsync(string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CoinMatch>>([]);

    /// <summary>
    /// The latest quote of a listing traded around the clock (a coin) and its price 24 hours earlier; null when the
    /// provider has no intraday prices. Only the listing symbol is sent.
    /// </summary>
    Task<Rolling24h?> GetRolling24hAsync(string symbol, CancellationToken ct) => Task.FromResult<Rolling24h?>(null);
}

/// <summary>Used when <c>MarketData:Provider</c> is <c>none</c>: no request is ever made.</summary>
public sealed class DisabledPriceHistorySource : IPriceHistorySource
{
    public string Name => "none";

    public bool Enabled => false;

    public Task<ListingMatch?> ResolveAsync(ListingQuery query, CancellationToken ct) =>
        Task.FromResult<ListingMatch?>(null);

    public Task<PriceSeries?> GetDailyClosesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<PriceSeries?>(null);
}
