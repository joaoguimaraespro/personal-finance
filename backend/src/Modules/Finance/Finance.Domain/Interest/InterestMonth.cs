using SharedKernel;

namespace Finance.Domain.Interest;

public enum InterestMonthStatus
{
    /// <summary>Only an estimate exists; it keeps being recalculated until the user reconciles the month.</summary>
    Estimated = 0,

    /// <summary>The user confirmed the estimate was what the bank paid.</summary>
    Confirmed = 1,

    /// <summary>The user entered the real amount, which replaced the estimate.</summary>
    Corrected = 2,
}

/// <summary>
/// One account-month of interest. While <see cref="InterestMonthStatus.Estimated"/> it points at the single
/// estimated ledger entry for the month; once reconciled it points at the real entry (or none when nothing was paid).
/// </summary>
public sealed class InterestMonth : Entity
{
    private InterestMonth() { }

    public Guid AccountId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public YearMonth Period => new(Year, Month);
    public InterestMonthStatus Status { get; private set; }

    /// <summary>Latest estimate, net of withholding, in the account's currency.</summary>
    public decimal EstimatedAmount { get; private set; }

    /// <summary>Gross estimate before withholding, for display.</summary>
    public decimal EstimatedGross { get; private set; }

    /// <summary>What the bank actually paid (net), once reconciled.</summary>
    public decimal? ActualAmount { get; private set; }

    /// <summary>The estimated entry while estimated; the real entry once reconciled (null if nothing was paid).</summary>
    public Guid? TransactionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public bool IsResolved => Status != InterestMonthStatus.Estimated;

    public static InterestMonth Start(Guid accountId, YearMonth period, DateTimeOffset now) => new()
    {
        AccountId = accountId,
        Year = period.Year,
        Month = period.Month,
        CreatedAtUtc = now,
    };

    public void UpdateEstimate(decimal net, decimal gross, Guid? estimateTransactionId)
    {
        if (IsResolved)
        {
            return;
        }

        EstimatedAmount = net;
        EstimatedGross = gross;
        TransactionId = estimateTransactionId;
    }

    /// <summary>Records what was really paid. Equal to the estimate counts as a confirmation.</summary>
    public void Resolve(decimal actual, Guid? realTransactionId, DateTimeOffset now)
    {
        Status = actual == EstimatedAmount ? InterestMonthStatus.Confirmed : InterestMonthStatus.Corrected;
        ActualAmount = actual;
        TransactionId = realTransactionId;
        ResolvedAtUtc = now;
    }
}
