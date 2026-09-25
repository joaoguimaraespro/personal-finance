using SharedKernel;

namespace Investments.Domain;

/// <summary>
/// Current holding of one security in one broker account, as reported by the broker. Records are replaced by
/// each sync; they are facts about an external account, not something this application can change.
/// </summary>
public sealed class Position
{
    private Position() { }

    public Guid AccountId { get; private set; }
    public Guid SecurityId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Average price paid, in the security's currency.</summary>
    public decimal AveragePrice { get; private set; }
    public decimal LastPrice { get; private set; }
    public DateTimeOffset PriceAsOfUtc { get; private set; }
    public DataSource Source { get; private set; }
    public DateTimeOffset SyncedAtUtc { get; private set; }

    public decimal CostBasis => Quantity * AveragePrice;
    public decimal MarketValue => Quantity * LastPrice;

    public static Position Report(Guid accountId, Guid securityId, decimal quantity, decimal averagePrice,
        decimal lastPrice, DateTimeOffset priceAsOf, DataSource source, DateTimeOffset syncedAt) => new()
    {
        AccountId = accountId,
        SecurityId = securityId,
        Quantity = quantity,
        AveragePrice = averagePrice,
        LastPrice = lastPrice,
        PriceAsOfUtc = priceAsOf,
        Source = source,
        SyncedAtUtc = syncedAt,
    };

    public void Update(decimal quantity, decimal averagePrice, decimal lastPrice, DateTimeOffset priceAsOf,
        DateTimeOffset syncedAt)
    {
        Quantity = quantity;
        AveragePrice = averagePrice;
        LastPrice = lastPrice;
        PriceAsOfUtc = priceAsOf;
        SyncedAtUtc = syncedAt;
    }
}

public enum TradeSide
{
    Buy = 0,
    Sell = 1,
}

/// <summary>An executed trade reported by a broker — a historical fact, never an instruction.</summary>
public sealed class Trade : Entity
{
    private Trade() { }

    public Guid AccountId { get; private set; }
    public Guid SecurityId { get; private set; }
    public TradeSide Side { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;

    /// <summary>Fees and taxes in the account's currency.</summary>
    public decimal Fees { get; private set; }
    public decimal Taxes { get; private set; }

    /// <summary>Fees + taxes converted to EUR at execution.</summary>
    public decimal CostsBase { get; private set; }

    /// <summary>Net cash impact in EUR (negative for buys, positive for sells), as reported or converted.</summary>
    public decimal BaseAmount { get; private set; }
    public decimal? RealizedPnlBase { get; private set; }
    public DateTimeOffset ExecutedAtUtc { get; private set; }
    public DataSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;

    public static Trade Record(Guid accountId, Guid securityId, TradeSide side, decimal quantity, decimal price,
        string currency, decimal fees, decimal taxes, decimal costsBase, decimal baseAmount, decimal? realizedPnlBase,
        DateTimeOffset executedAt, DataSource source, string externalId) => new()
    {
        CostsBase = costsBase,
        AccountId = accountId,
        SecurityId = securityId,
        Side = side,
        Quantity = quantity,
        Price = price,
        Currency = currency,
        Fees = fees,
        Taxes = taxes,
        BaseAmount = baseAmount,
        RealizedPnlBase = realizedPnlBase,
        ExecutedAtUtc = executedAt,
        Source = source,
        ExternalId = externalId,
    };
}

public sealed class Dividend : Entity
{
    private Dividend() { }

    public Guid AccountId { get; private set; }
    public Guid SecurityId { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal WithholdingTax { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public decimal NetBaseAmount { get; private set; }

    /// <summary>True when withholding tax was inferred (gross × quantity − net) rather than reported.</summary>
    public bool WithholdingDerived { get; private set; }
    public DataSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;

    public static Dividend Record(Guid accountId, Guid securityId, DateOnly paidOn, decimal gross,
        decimal withholding, decimal net, string currency, decimal netBase, bool withholdingDerived,
        DataSource source, string externalId) => new()
    {
        AccountId = accountId,
        SecurityId = securityId,
        PaidOn = paidOn,
        GrossAmount = gross,
        WithholdingTax = withholding,
        NetAmount = net,
        Currency = currency,
        NetBaseAmount = netBase,
        WithholdingDerived = withholdingDerived,
        Source = source,
        ExternalId = externalId,
    };

    /// <summary>A later, more complete report (e.g. CSV with withholding tax) replaces derived values.</summary>
    public void ApplyReportedWithholding(decimal gross, decimal withholding)
    {
        GrossAmount = gross;
        WithholdingTax = withholding;
        WithholdingDerived = false;
    }
}

public enum CashMovementType
{
    Deposit = 0,
    Withdrawal = 1,
    Fee = 2,
    Interest = 3,
    Tax = 4,
    FxConversion = 5,
    Other = 6,
}

/// <summary>Cash flows on a broker account. Deposits/withdrawals are the external flows used by TWR/XIRR.</summary>
public sealed class CashMovement : Entity
{
    private CashMovement() { }

    public Guid AccountId { get; private set; }
    public CashMovementType Type { get; private set; }

    /// <summary>Signed from the account's perspective: deposits positive, withdrawals and fees negative.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public decimal BaseAmount { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Description { get; private set; }
    public DataSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;

    public bool IsExternalFlow => Type is CashMovementType.Deposit or CashMovementType.Withdrawal;

    public static CashMovement Record(Guid accountId, CashMovementType type, decimal amount, string currency,
        decimal baseAmount, DateTimeOffset occurredAt, string? description, DataSource source, string externalId) =>
        new()
        {
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Currency = currency,
            BaseAmount = baseAmount,
            OccurredAtUtc = occurredAt,
            Description = description,
            Source = source,
            ExternalId = externalId,
        };
}
