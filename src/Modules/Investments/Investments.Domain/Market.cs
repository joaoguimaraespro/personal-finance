using SharedKernel;

namespace Investments.Domain;

public sealed class MarketPrice
{
    private MarketPrice() { }

    public Guid SecurityId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Close { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public DataSource Source { get; private set; }

    public static MarketPrice Create(Guid securityId, DateOnly date, decimal close, string currency, DataSource source) =>
        new() { SecurityId = securityId, Date = date, Close = close, Currency = currency, Source = source };

    public void Update(decimal close) => Close = close;
}

/// <summary>ECB reference rate: 1 EUR = <see cref="Rate"/> units of <see cref="Quote"/>.</summary>
public sealed class FxRate
{
    private FxRate() { }

    public DateOnly Date { get; private set; }
    public string Quote { get; private set; } = null!;
    public decimal Rate { get; private set; }
    public string Source { get; private set; } = "ECB";

    public static FxRate Create(DateOnly date, string quote, decimal rate, string source = "ECB") =>
        new() { Date = date, Quote = quote, Rate = rate, Source = source };

    /// <summary>EUR value of one unit of the quote currency.</summary>
    public decimal ToEurFactor => 1m / Rate;
}

/// <summary>End-of-day valuation of one broker account. Needed because brokers do not all expose history.</summary>
public sealed class PortfolioSnapshot
{
    private PortfolioSnapshot() { }

    public Guid AccountId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal MarketValueBase { get; private set; }
    public decimal CashBase { get; private set; }

    /// <summary>Deposits minus withdrawals on that day (EUR). Used to separate performance from contributions.</summary>
    public decimal NetFlowBase { get; private set; }
    public string Origin { get; private set; } = null!;

    public decimal TotalBase => MarketValueBase + CashBase;

    public static PortfolioSnapshot Create(Guid accountId, DateOnly date, decimal marketValue, decimal cash,
        decimal netFlow, string origin) => new()
    {
        AccountId = accountId,
        Date = date,
        MarketValueBase = marketValue,
        CashBase = cash,
        NetFlowBase = netFlow,
        Origin = origin,
    };

    public void Update(decimal marketValue, decimal cash, decimal netFlow, string origin)
    {
        MarketValueBase = marketValue;
        CashBase = cash;
        NetFlowBase = netFlow;
        Origin = origin;
    }
}

/// <summary>Broker-reported cash balance per account (latest).</summary>
public sealed class CashBalance
{
    private CashBalance() { }

    public Guid AccountId { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public decimal Amount { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }

    public static CashBalance Report(Guid accountId, string currency, decimal amount, DateTimeOffset syncedAt) =>
        new() { AccountId = accountId, Currency = currency, Amount = amount, SyncedAtUtc = syncedAt };

    public void Update(decimal amount, DateTimeOffset syncedAt)
    {
        Amount = amount;
        SyncedAtUtc = syncedAt;
    }
}

/// <summary>User-defined target mix. Informational only: the application never rebalances.</summary>
public sealed class TargetAllocation
{
    private TargetAllocation() { }

    public AssetClass AssetClass { get; private set; }
    public decimal Percent { get; private set; }

    public static TargetAllocation Create(AssetClass assetClass, decimal percent) =>
        new() { AssetClass = assetClass, Percent = percent };
}
