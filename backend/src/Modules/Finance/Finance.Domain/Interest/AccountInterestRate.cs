using Finance.Domain.Accounts;
using SharedKernel;

namespace Finance.Domain.Interest;

/// <summary>
/// One period of an account's nominal annual rate (TANB). Periods are append-only: a rate change adds a new period
/// from its effective date and never rewrites earlier ones, so past estimates stay explainable.
/// </summary>
public sealed class AccountInterestRate : Entity
{
    /// <summary>Portuguese "taxa liberatória" on deposit interest, withheld at source.</summary>
    public const decimal DefaultWithholdingPercent = 28m;

    private AccountInterestRate() { }

    public Guid AccountId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Gross nominal annual rate in percent (2.25 means 2.25 %).</summary>
    public decimal AnnualRatePercent { get; private set; }

    /// <summary>Tax withheld on the interest, in percent.</summary>
    public decimal WithholdingPercent { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Result<AccountInterestRate> Create(Account account, DateOnly effectiveFrom, decimal annualRatePercent,
        decimal? withholdingPercent, DateTimeOffset now)
    {
        if (!account.SupportsInterest)
        {
            return Result.Failure<AccountInterestRate>(InterestErrors.NotSupported);
        }

        if (annualRatePercent is < 0 or > 100)
        {
            return Result.Failure<AccountInterestRate>(InterestErrors.InvalidRate);
        }

        var withholding = withholdingPercent ?? DefaultWithholdingPercent;
        if (withholding is < 0 or > 100)
        {
            return Result.Failure<AccountInterestRate>(InterestErrors.InvalidWithholding);
        }

        return new AccountInterestRate
        {
            AccountId = account.Id,
            EffectiveFrom = effectiveFrom,
            AnnualRatePercent = annualRatePercent,
            WithholdingPercent = withholding,
            CreatedAtUtc = now,
        };
    }

    public RatePeriod ToPeriod() => new(EffectiveFrom, AnnualRatePercent, WithholdingPercent);
}

public static class InterestErrors
{
    public static readonly Error NotSupported = Error.Validation("Interest.Account",
        "Interest rates can only be set on savings and bank accounts.");

    public static readonly Error InvalidRate = Error.Validation("Interest.Rate", "The rate must be between 0 and 100 %.");

    public static readonly Error InvalidWithholding =
        Error.Validation("Interest.Withholding", "Withholding must be between 0 and 100 %.");

    public static readonly Error DuplicatePeriod = Error.Conflict("Interest.Period",
        "A rate already starts on that date. Rates are never rewritten: pick another date.");

    public static readonly Error OnlyLatestRemovable = Error.Conflict("Interest.Period",
        "Only the most recent rate period can be removed.");

    public static readonly Error RateNotFound = Error.NotFound("Interest.RateNotFound", "Rate period not found.");

    public static readonly Error MonthNotFound = Error.NotFound("Interest.MonthNotFound", "Interest month not found.");

    public static readonly Error MonthOpen =
        Error.Conflict("Interest.MonthOpen", "The month has not ended yet; it can be reconciled once it closes.");

    public static readonly Error AlreadyResolved =
        Error.Conflict("Interest.Resolved", "This month's interest has already been reconciled.");

    public static readonly Error FxRateRequired = Error.Validation("Interest.FxRate",
        "An exchange rate to EUR is required for interest on a foreign-currency account.");
}
