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

    /// <summary>What the holding really cost in EUR, as the broker reports it (null: not reported).</summary>
    public decimal? CostBase { get; private set; }

    /// <summary>The holding's value in EUR at the broker's own rate, valid while <see cref="LastPrice"/> is the broker's.</summary>
    public decimal? ValueBase { get; private set; }

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

    /// <summary>A newer public price between syncs (end-of-day brokers): quantity, cost and sync time stay.</summary>
    public void Reprice(decimal lastPrice, DateTimeOffset priceAsOf)
    {
        LastPrice = lastPrice;
        PriceAsOfUtc = priceAsOf;
        ValueBase = null; // the broker's EUR value was for its own price
    }

    /// <summary>The broker's own EUR figures for the holding, when it reports them.</summary>
    public void SetBase(decimal? cost, decimal? value)
    {
        CostBase = cost;
        ValueBase = value;
    }

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

/// <summary>
/// Where a synced deposit or withdrawal stands in the owner's ledger: the money came from (or went to) one of their
/// accounts, and only they know which. Other cash movements (fees, interest) are always <see cref="Counted"/>.
/// </summary>
public enum CashMovementReview
{
    /// <summary>Counted towards the broker's bucket as it is, unless the ledger already records that money.</summary>
    Counted = 0,

    /// <summary>Waits for the owner to say which account the money came from (or went to).</summary>
    Pending = 1,

    /// <summary>Recorded in the ledger as <see cref="CashMovement.RecordedTransactionId"/>.</summary>
    Recorded = 2,

    /// <summary>The owner said it needs no entry (e.g. money moved between brokers, or already recorded).</summary>
    Ignored = 3,
}

/// <summary>Cash flows on a broker account. Deposits/withdrawals are the external flows used by TWR/XIRR.</summary>
public sealed class CashMovement : Entity
{
    /// <summary>
    /// A deposit or withdrawal synced this recently asks where the money came from; older history (a first sync, a
    /// re-sync) is counted as it is, so connecting a broker does not bring years of questions.
    /// </summary>
    public const int ReviewWindowDays = 35;

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

    public CashMovementReview Review { get; private set; }
    public Guid? RecordedTransactionId { get; private set; }

    public bool IsExternalFlow => Type is CashMovementType.Deposit or CashMovementType.Withdrawal;

    /// <param name="now">When the sync runs: a recent deposit or withdrawal waits for review (<see cref="ReviewWindowDays"/>).</param>
    public static CashMovement Record(Guid accountId, CashMovementType type, decimal amount, string currency,
        decimal baseAmount, DateTimeOffset occurredAt, string? description, DataSource source, string externalId,
        DateTimeOffset? now = null) =>
        new()
        {
            Review = source != DataSource.Manual && type is CashMovementType.Deposit or CashMovementType.Withdrawal &&
                     now is { } n && occurredAt >= n.AddDays(-ReviewWindowDays)
                ? CashMovementReview.Pending
                : CashMovementReview.Counted,
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

    public void MarkRecorded(Guid transactionId)
    {
        Review = CashMovementReview.Recorded;
        RecordedTransactionId = transactionId;
    }

    public void Ignore()
    {
        Review = CashMovementReview.Ignored;
        RecordedTransactionId = null;
    }

    /// <summary>Asks again: whatever recorded it stays in the ledger, it is just no longer linked.</summary>
    public void Reopen()
    {
        Review = CashMovementReview.Pending;
        RecordedTransactionId = null;
    }
}
