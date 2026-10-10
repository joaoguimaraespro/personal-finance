using Finance.Domain.Accounts;
using SharedKernel;

namespace Finance.Domain.Loans;

public enum LoanRateType
{
    Fixed = 0,

    /// <summary>An index (e.g. Euribor 12M) plus a spread, revised every few months.</summary>
    Variable = 1,
}

public enum PrepaymentMode
{
    /// <summary>Same instalment, fewer months.</summary>
    ReduceTerm = 0,

    /// <summary>Same end date, lower instalment.</summary>
    ReducePayment = 1,
}

/// <summary>
/// The credit terms of a loan account (a mortgage, a car or personal loan): what was borrowed, from when, for how
/// long and at what rate. With terms, the account's balance comes from the amortisation plan, so the debt is right
/// without booking every instalment; the instalments paid from the bank are still recorded in the ledger.
/// </summary>
public sealed class Loan : Entity, IAuditable
{
    private readonly List<LoanRate> _rates = [];
    private readonly List<LoanPrepayment> _prepayments = [];

    private Loan() { }

    public Guid AccountId { get; private set; }
    public decimal Principal { get; private set; }
    public DateOnly FirstPaymentOn { get; private set; }
    public int TermMonths { get; private set; }
    public LoanRateType RateType { get; private set; }

    /// <summary>Variable rate: how often it is revised (3, 6 or 12 months).</summary>
    public int? RevisionMonths { get; private set; }

    /// <summary>Variable rate: the index's name, e.g. "Euribor 12M".</summary>
    public string? IndexName { get; private set; }

    /// <summary>Variable rate: the bank's spread over the index, in percent.</summary>
    public decimal? SpreadPercent { get; private set; }

    /// <summary>The bank account the instalments are paid from: set, each instalment is proposed for booking.</summary>
    public Guid? PaymentAccountId { get; private set; }

    /// <summary>Instalments up to this number are booked or skipped; later ones due by today are pending.</summary>
    public int LastHandledInstalment { get; private set; }

    public IReadOnlyList<LoanRate> Rates => _rates;
    public IReadOnlyList<LoanPrepayment> Prepayments => _prepayments;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static Result<Loan> Create(Account account, LoanTermsDraft d)
    {
        if (account.Kind != AccountKind.Loan)
        {
            return Result.Failure<Loan>(LoanErrors.NotALoanAccount);
        }

        var loan = new Loan { AccountId = account.Id };
        return loan.Apply(d) is { } error ? Result.Failure<Loan>(error) : loan;
    }

    public Error? Apply(LoanTermsDraft d)
    {
        if (d.Principal <= 0 || d.TermMonths is < 1 or > LoanSchedule.MaxMonths)
        {
            return LoanErrors.InvalidTerms;
        }

        if (d.RateType == LoanRateType.Variable && d.RevisionMonths is not (1 or 3 or 6 or 12))
        {
            return LoanErrors.InvalidRevision;
        }

        Principal = d.Principal;
        FirstPaymentOn = d.FirstPaymentOn;
        TermMonths = d.TermMonths;
        RateType = d.RateType;
        RevisionMonths = d.RateType == LoanRateType.Variable ? d.RevisionMonths : null;
        IndexName = d.RateType == LoanRateType.Variable ? d.IndexName?.Trim() : null;
        SpreadPercent = d.RateType == LoanRateType.Variable ? d.SpreadPercent : null;
        return null;
    }

    /// <summary>A rate from a date on (the initial rate, or a revision). One per date: a second one replaces it.</summary>
    public Error? SetRate(DateOnly effectiveFrom, decimal annualRatePercent, decimal? indexRatePercent)
    {
        if (annualRatePercent is < 0 or > 50)
        {
            return LoanErrors.InvalidRate;
        }

        _rates.RemoveAll(r => r.EffectiveFrom == effectiveFrom);
        _rates.Add(LoanRate.Create(Id, effectiveFrom, annualRatePercent, indexRatePercent));
        return null;
    }

    public bool RemoveRate(Guid rateId) => _rates.RemoveAll(r => r.Id == rateId) > 0;

    /// <summary>
    /// Instalments are paid from <paramref name="accountId"/> from now on: those already due before
    /// <paramref name="today"/> are not proposed (they were paid before the app knew), later ones are. Null stops it.
    /// </summary>
    public void PayFrom(Guid? accountId, DateOnly today)
    {
        if (accountId is not null && PaymentAccountId is null)
        {
            LastHandledInstalment = Plan().Where(i => i.Date < today && i.Payment > 0).Select(i => i.Number)
                .DefaultIfEmpty(0).Max();
        }

        PaymentAccountId = accountId;
    }

    /// <summary>Instalments due by <paramref name="today"/> and not yet booked or skipped, oldest first.</summary>
    public IReadOnlyList<Instalment> PendingInstalments(DateOnly today) => PaymentAccountId is null
        ? []
        : Plan().Where(i => i.Number > LastHandledInstalment && i.Date <= today && i.Payment > 0).ToList();

    /// <summary>Booked or skipped: only the oldest pending instalment can be handled, so none is left behind.</summary>
    public Error? Handle(int number, DateOnly today)
    {
        var pending = PendingInstalments(today);
        if (pending.Count == 0 || pending[0].Number != number)
        {
            return LoanErrors.NotNextInstalment;
        }

        LastHandledInstalment = number;
        return null;
    }

    /// <summary>Variable rate: the revision dates up to <paramref name="until"/> (the first instalment's rate excluded).</summary>
    public IEnumerable<DateOnly> RevisionDatesUntil(DateOnly until)
    {
        if (RateType != LoanRateType.Variable || RevisionMonths is not { } every)
        {
            yield break;
        }

        for (var d = FirstPaymentOn.AddMonths(every); d <= until; d = d.AddMonths(every))
        {
            yield return d;
        }
    }

    public Error? AddPrepayment(DateOnly on, decimal amount, PrepaymentMode mode)
    {
        if (amount <= 0)
        {
            return LoanErrors.InvalidPrepayment;
        }

        _prepayments.Add(LoanPrepayment.Create(Id, on, amount, mode));
        return null;
    }

    public bool RemovePrepayment(Guid id) => _prepayments.RemoveAll(p => p.Id == id) > 0;

    public IReadOnlyList<Instalment> Plan(IEnumerable<Prepayment>? extra = null) =>
        LoanSchedule.Build(Principal, FirstPaymentOn, TermMonths,
            _rates.Select(r => new RatePeriod(r.EffectiveFrom, r.AnnualRatePercent)).ToList(),
            _prepayments.Select(p => new Prepayment(p.On, p.Amount, p.Mode)).Concat(extra ?? []).ToList());

    /// <summary>Variable rate: the next revision after <paramref name="today"/>, counted from the first instalment.</summary>
    public DateOnly? NextRevisionAfter(DateOnly today)
    {
        if (RateType != LoanRateType.Variable || RevisionMonths is not { } every)
        {
            return null;
        }

        var next = FirstPaymentOn;
        while (next <= today)
        {
            next = next.AddMonths(every);
        }

        return next;
    }
}

public sealed record LoanTermsDraft(decimal Principal, DateOnly FirstPaymentOn, int TermMonths, LoanRateType RateType,
    int? RevisionMonths, string? IndexName, decimal? SpreadPercent);

public sealed class LoanRate : Entity
{
    private LoanRate() { }

    public Guid LoanId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Nominal annual rate (TAN) in percent: for a variable loan, index + spread.</summary>
    public decimal AnnualRatePercent { get; private set; }

    /// <summary>Variable loans: the index value used (e.g. Euribor on the revision date), for reference.</summary>
    public decimal? IndexRatePercent { get; private set; }

    internal static LoanRate Create(Guid loanId, DateOnly from, decimal annual, decimal? index) =>
        new() { LoanId = loanId, EffectiveFrom = from, AnnualRatePercent = annual, IndexRatePercent = index };
}

public sealed class LoanPrepayment : Entity
{
    private LoanPrepayment() { }

    public Guid LoanId { get; private set; }
    public DateOnly On { get; private set; }
    public decimal Amount { get; private set; }
    public PrepaymentMode Mode { get; private set; }

    internal static LoanPrepayment Create(Guid loanId, DateOnly on, decimal amount, PrepaymentMode mode) =>
        new() { LoanId = loanId, On = on, Amount = amount, Mode = mode };
}

public static class LoanErrors
{
    public static readonly Error NotFound = Error.NotFound("Loan.NotFound", "No loan terms for this account.");

    public static readonly Error NotALoanAccount =
        Error.Validation("Loan.Account", "Loan terms belong to an account of type Loan.");

    public static readonly Error InvalidTerms =
        Error.Validation("Loan.Terms", "The amount must be positive and the term between 1 and 720 months.");

    public static readonly Error InvalidRevision =
        Error.Validation("Loan.Revision", "A variable rate is revised every 1, 3, 6 or 12 months.");

    public static readonly Error InvalidRate = Error.Validation("Loan.Rate", "The rate must be between 0 and 50 %.");

    public static readonly Error InvalidPrepayment =
        Error.Validation("Loan.Prepayment", "An early repayment must be a positive amount.");

    public static readonly Error NoRate = Error.Validation("Loan.NoRate", "Add the loan's rate first.");

    public static readonly Error NotNextInstalment =
        Error.Conflict("Loan.Instalment", "Only the oldest pending instalment can be booked or skipped.");

    public static readonly Error InvalidPaymentAccount =
        Error.Validation("Loan.PaymentAccount", "Instalments are paid from a bank, savings or cash account.");
}
