using Investments.Domain;

namespace Investments.Application.Sync;

// Provider-neutral records. Broker-specific JSON/XML shapes never cross this boundary.

public sealed record SecurityReport(string BrokerSymbol, string? Isin, string Symbol, string? Exchange, string Name,
    string Currency, AssetClass AssetClass);

public sealed record PositionReport(SecurityReport Security, decimal Quantity, decimal AveragePrice, decimal LastPrice,
    DateTimeOffset PriceAsOf);

public sealed record TradeReport(string ExternalId, SecurityReport Security, TradeSide Side, decimal Quantity,
    decimal Price, string Currency, decimal Fees, decimal Taxes, string FeesCurrency, decimal? NetAmountInAccountCurrency,
    string AccountCurrency, decimal? RealizedPnlInAccountCurrency, DateTimeOffset ExecutedAt);

public sealed record DividendReport(string ExternalId, SecurityReport Security, DateOnly PaidOn, decimal? GrossAmount,
    decimal? WithholdingTax, decimal NetAmount, string Currency, decimal? GrossPerShare, decimal? Quantity);

public sealed record CashMovementReport(string ExternalId, CashMovementType Type, decimal Amount, string Currency,
    DateTimeOffset OccurredAt, string? Description);

public sealed record SnapshotReport(DateOnly Date, decimal TotalValue, decimal Cash, string Currency,
    decimal? NetFlow);

public sealed record SyncCounts(int Imported, int Updated, int Ignored)
{
    public static readonly SyncCounts None = new(0, 0, 0);

    public static SyncCounts operator +(SyncCounts a, SyncCounts b) =>
        new(a.Imported + b.Imported, a.Updated + b.Updated, a.Ignored + b.Ignored);
}
