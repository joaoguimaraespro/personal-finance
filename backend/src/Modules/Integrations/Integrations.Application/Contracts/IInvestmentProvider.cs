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
