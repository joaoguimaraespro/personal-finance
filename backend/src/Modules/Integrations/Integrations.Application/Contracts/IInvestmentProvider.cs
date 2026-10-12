using Investments.Application.Sync;

namespace Integrations.Application.Contracts;

/// <summary>
/// Read-only view of one broker account. Implementations can fetch; they cannot act. There is deliberately no
/// method for orders, transfers or settings, and each implementation's HTTP client is allow-listed to read endpoints.
/// </summary>
public interface IInvestmentProvider
{
    BrokerKind Kind { get; }

    Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct);

    Task<IReadOnlyList<PositionReport>> GetPositionsAsync(CancellationToken ct);

    Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since, CancellationToken ct);

    Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct);
}

public enum BrokerKind
{
    Trading212 = 0,
    InteractiveBrokers = 1,
    Binance = 2,
    Demo = 99,
}

/// <summary>Current account state plus any valuation history the broker provides (IBKR NAV).</summary>
public sealed record AccountSnapshot(string Currency, decimal Cash, decimal TotalValue,
    IReadOnlyList<SnapshotReport> History);

/// <summary>A trade or a cash movement, normalised.</summary>
public sealed record InvestmentTransaction(TradeReport? Trade, CashMovementReport? Cash)
{
    public static InvestmentTransaction Of(TradeReport trade) => new(trade, null);

    public static InvestmentTransaction Of(CashMovementReport cash) => new(null, cash);
}

/// <summary>A failure the user must fix (expired token, revoked key, wrong query id). Retrying will not help.</summary>
public sealed class ProviderConfigurationException(string message) : Exception(message);

/// <summary>A temporary failure (rate limit, statement still generating, network).</summary>
public sealed class ProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A provider that rebuilds each position's cost from history and can show how (read-only, on demand).</summary>
public interface ICostBreakdownProvider
{
    Task<IReadOnlyList<CostBreakdown>> GetCostBreakdownAsync(CancellationToken ct);
}

/// <summary>
/// How one coin's average cost was reached: what is held, what the history accounts for (<paramref name="Tracked"/>,
/// in minus out) and each kind of movement. A null <paramref name="AverageCost"/> means no history: the cost of the
/// first sync is kept.
/// </summary>
public sealed record CostBreakdown(string Asset, decimal Held, decimal Tracked, decimal? AverageCost, decimal EurPrice,
    IReadOnlyList<CostSourceLine> Sources);

/// <param name="Kind">trade, card, convert, reward, received, sent.</param>
/// <param name="Quantity">Coins in (positive) or out (negative).</param>
/// <param name="EurValue">What the coins in were counted at; 0 for coins out (they leave at the average).</param>
public sealed record CostSourceLine(string Kind, int Count, decimal Quantity, decimal EurValue);

/// <summary>
/// A provider that keeps what it has read between syncs (e.g. a long history it reads only once). The sync sets
/// <see cref="HistoryState"/> from the connection before reading and stores it back after a successful sync.
/// </summary>
public interface IHistoryStateProvider
{
    string? HistoryState { get; set; }
}
