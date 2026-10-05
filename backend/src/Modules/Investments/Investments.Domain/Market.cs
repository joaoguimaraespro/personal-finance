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

/// <summary>
/// Which listing of a security a public price provider uses for it (e.g. Yahoo "VWCE.DE"), and which days have
/// already been fetched, so each day is downloaded once. <see cref="Symbol"/> is null when nothing was found.
/// </summary>
public sealed class PriceListing
{
    private PriceListing() { }

    public Guid SecurityId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string? Symbol { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset ResolvedAtUtc { get; private set; }
    public DateOnly? CoveredFrom { get; private set; }
    public DateOnly? CoveredTo { get; private set; }

    /// <summary>
    /// A coin's price 24 hours before its latest quote, in the security's currency. Coins trade around the clock,
    /// so their change is over a rolling 24 hours, as exchanges show it. Refreshed with the coin's price.
    /// </summary>
    public decimal? Reference24hPrice { get; private set; }

    /// <summary>When <see cref="Reference24hPrice"/> was quoted (24 hours before the latest quote).</summary>
    public DateTimeOffset? Reference24hAtUtc { get; private set; }

    public static PriceListing Create(Guid securityId, string provider, string? symbol, string? currency,
        DateTimeOffset at) => new()
    {
        SecurityId = securityId,
        Provider = provider,
        Symbol = symbol,
        Currency = currency,
        ResolvedAtUtc = at,
    };

    /// <summary>A new resolution (other provider, or a retry after nothing was found) forgets the fetched range.</summary>
    public void Resolve(string provider, string? symbol, string? currency, DateTimeOffset at)
    {
        if (Provider != provider || Symbol != symbol)
        {
            CoveredFrom = null;
            CoveredTo = null;
        }

        Provider = provider;
        Symbol = symbol;
        Currency = currency;
        ResolvedAtUtc = at;
    }

    public void Cover(DateOnly from, DateOnly to)
    {
        CoveredFrom = CoveredFrom is { } f && f < from ? f : from;
        CoveredTo = CoveredTo is { } t && t > to ? t : to;
    }

    public void SetReference24h(decimal price, DateTimeOffset at)
    {
        Reference24hPrice = price;
        Reference24hAtUtc = at;
    }
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

    /// <summary>
    /// Reconstructed days only: how many holdings were valued from a trade price because no public close was
    /// available, so the UI can say the history is partly estimated.
    /// </summary>
    public int EstimatedHoldings { get; private set; }

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

    public static PortfolioSnapshot Reconstructed(Guid accountId, DateOnly date, decimal marketValue, decimal cash,
        decimal netFlow, string origin, int estimatedHoldings)
    {
        var snapshot = Create(accountId, date, marketValue, cash, netFlow, origin);
        snapshot.EstimatedHoldings = estimatedHoldings;
        return snapshot;
    }

    public void Update(decimal marketValue, decimal cash, decimal netFlow, string origin)
    {
        MarketValueBase = marketValue;
        CashBase = cash;
        NetFlowBase = netFlow;
        Origin = origin;
        EstimatedHoldings = 0;
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
