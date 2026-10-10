using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Accounts;
using Finance.Domain.Loans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Loans;

public sealed record LoanTermsRequest(decimal Principal, DateOnly FirstPaymentOn, int TermMonths, LoanRateType RateType,
    int? RevisionMonths, string? IndexName, decimal? SpreadPercent, decimal? InitialRatePercent,
    decimal? InitialIndexPercent);

/// <summary>A rate from a date: the TAN, or for a variable loan the index (the loan's spread is added).</summary>
public sealed record LoanRateRequest(DateOnly EffectiveFrom, decimal? AnnualRatePercent, decimal? IndexRatePercent);

public sealed record PrepaymentRequest(DateOnly On, decimal Amount, PrepaymentMode Mode);

public sealed record LoanRateDto(Guid Id, DateOnly EffectiveFrom, decimal AnnualRatePercent, decimal? IndexRatePercent);

public sealed record LoanPrepaymentDto(Guid Id, DateOnly On, decimal Amount, PrepaymentMode Mode);

public sealed record LoanSummaryDto(
    Guid AccountId,
    string AccountName,
    decimal Principal,
    DateOnly FirstPaymentOn,
    int TermMonths,
    LoanRateType RateType,
    int? RevisionMonths,
    string? IndexName,
    decimal? SpreadPercent,
    decimal Outstanding,
    decimal PaidOffShare,
    int InstalmentsPaid,
    int InstalmentsLeft,
    Instalment? Next,
    DateOnly? EndDate,
    decimal InterestPaid,
    decimal InterestLeft,
    decimal CurrentRatePercent,
    DateOnly? NextRevision);

public sealed record LoanDetailDto(
    LoanSummaryDto Summary,
    IReadOnlyList<LoanRateDto> Rates,
    IReadOnlyList<LoanPrepaymentDto> Prepayments,
    IReadOnlyList<Instalment> Plan);

/// <summary>What an early repayment would change, against the current plan.</summary>
public sealed record PrepaymentSimulationDto(
    DateOnly? EndDateBefore,
    DateOnly? EndDateAfter,
    int MonthsSaved,
    decimal PaymentBefore,
    decimal PaymentAfter,
    decimal InterestBefore,
    decimal InterestAfter,
    decimal InterestSaved);

public sealed class LoanTermsValidator : AbstractValidator<LoanTermsRequest>
{
    public LoanTermsValidator()
    {
        RuleFor(x => x.Principal).GreaterThan(0).LessThanOrEqualTo(100_000_000m);
        RuleFor(x => x.TermMonths).InclusiveBetween(1, LoanSchedule.MaxMonths);
        RuleFor(x => x.RateType).IsInEnum();
        RuleFor(x => x.RevisionMonths).Must(m => m is 1 or 3 or 6 or 12)
            .When(x => x.RateType == LoanRateType.Variable).WithMessage("Revised every 1, 3, 6 or 12 months.");
        RuleFor(x => x.SpreadPercent).InclusiveBetween(-5, 20).When(x => x.SpreadPercent is not null);
        RuleFor(x => x.IndexName).MaximumLength(40);
    }
}

/// <summary>Loan terms of Loan accounts: plan, where it stands, rate revisions and early repayments.</summary>
public static class LoanEndpoints
{
    public static void MapLoans(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/loans").WithTags("Loans");

        group.MapGet("/", async (IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = Today(clock);
            var loans = await LoadAllAsync(db, ct);
            var names = await db.Accounts.AsNoTracking().Where(a => loans.Select(l => l.AccountId).Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => (a.Name, a.ArchivedAtUtc), ct);
            return Results.Ok(loans.Where(l => names.TryGetValue(l.AccountId, out var n) && n.ArchivedAtUtc is null)
                .Select(l => Summary(l, names[l.AccountId].Name, today)).ToList());
        });

        group.MapGet("/{accountId:guid}", async (Guid accountId, IFinanceDb db, TimeProvider clock,
            CancellationToken ct) =>
        {
            var (loan, account) = await FindAsync(db, accountId, ct);
            return loan is null || account is null
                ? ResultHttp.Problem(LoanErrors.NotFound)
                : Results.Ok(Detail(loan, account.Name, Today(clock)));
        });

        // Creates or updates the terms (the first save also records the initial rate).
        group.MapPut("/{accountId:guid}", async (Guid accountId, LoanTermsRequest req, IFinanceDb db,
            CancellationToken ct) =>
        {
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                return ResultHttp.Problem(LoanErrors.NotALoanAccount);
            }

            var draft = new LoanTermsDraft(req.Principal, req.FirstPaymentOn, req.TermMonths, req.RateType,
                req.RevisionMonths, req.IndexName, req.SpreadPercent);
            var loan = await db.Loans.Include(l => l.Rates).Include(l => l.Prepayments)
                .FirstOrDefaultAsync(l => l.AccountId == accountId, ct);
            if (loan is null)
            {
                var created = Loan.Create(account, draft);
                if (created.IsFailure)
                {
                    return ResultHttp.Problem(created.Error);
                }

                loan = created.Value;
                db.Loans.Add(loan);
            }
            else if (loan.Apply(draft) is { } error)
            {
                return ResultHttp.Problem(error);
            }

            if (InitialRate(req) is { } rate && loan.Rates.All(r => r.EffectiveFrom != req.FirstPaymentOn))
            {
                if (loan.SetRate(req.FirstPaymentOn, rate, req.InitialIndexPercent) is { } rateError)
                {
                    return ResultHttp.Problem(rateError);
                }
            }

            if (loan.Rates.Count == 0)
            {
                return ResultHttp.Problem(LoanErrors.NoRate);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<LoanTermsRequest>();

        group.MapDelete("/{accountId:guid}", async (Guid accountId, IFinanceDb db, CancellationToken ct) =>
        {
            await db.Loans.Where(l => l.AccountId == accountId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/{accountId:guid}/rates", async (Guid accountId, LoanRateRequest req, IFinanceDb db,
            CancellationToken ct) =>
        {
            var (loan, _) = await FindAsync(db, accountId, ct, tracked: true);
            if (loan is null)
            {
                return ResultHttp.Problem(LoanErrors.NotFound);
            }

            // Variable loans may give the index only: the TAN is index + the loan's spread.
            var annual = req.AnnualRatePercent ??
                         (req.IndexRatePercent is { } index ? index + (loan.SpreadPercent ?? 0) : null);
            if (annual is null || loan.SetRate(req.EffectiveFrom, annual.Value, req.IndexRatePercent) is { } error)
            {
                return ResultHttp.Problem(annual is null ? LoanErrors.InvalidRate : LoanErrors.InvalidRate);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/{accountId:guid}/rates/{rateId:guid}", async (Guid accountId, Guid rateId, IFinanceDb db,
            CancellationToken ct) =>
        {
            var (loan, _) = await FindAsync(db, accountId, ct, tracked: true);
            if (loan is null)
            {
                return ResultHttp.Problem(LoanErrors.NotFound);
            }

            // The plan needs a rate: the last one stays.
            if (loan.Rates.Count > 1)
            {
                loan.RemoveRate(rateId);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });

        group.MapPost("/{accountId:guid}/prepayments", async (Guid accountId, PrepaymentRequest req, IFinanceDb db,
            CancellationToken ct) =>
        {
            var (loan, _) = await FindAsync(db, accountId, ct, tracked: true);
            if (loan is null)
            {
                return ResultHttp.Problem(LoanErrors.NotFound);
            }

            if (loan.AddPrepayment(req.On, req.Amount, req.Mode) is { } error)
            {
                return ResultHttp.Problem(error);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/{accountId:guid}/prepayments/{id:guid}", async (Guid accountId, Guid id, IFinanceDb db,
            CancellationToken ct) =>
        {
            var (loan, _) = await FindAsync(db, accountId, ct, tracked: true);
            if (loan is null)
            {
                return ResultHttp.Problem(LoanErrors.NotFound);
            }

            loan.RemovePrepayment(id);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // "What if I repay X on date Y?": nothing is saved.
        group.MapPost("/{accountId:guid}/simulate", async (Guid accountId, PrepaymentRequest req, IFinanceDb db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var (loan, _) = await FindAsync(db, accountId, ct);
            if (loan is null)
            {
                return ResultHttp.Problem(LoanErrors.NotFound);
            }

            if (req.Amount <= 0)
            {
                return ResultHttp.Problem(LoanErrors.InvalidPrepayment);
            }

            return Results.Ok(Simulate(loan, req, Today(clock)));
        });
    }

    public static PrepaymentSimulationDto Simulate(Loan loan, PrepaymentRequest req, DateOnly today)
    {
        var before = loan.Plan();
        var after = loan.Plan([new Prepayment(req.On, req.Amount, req.Mode)]);
        var from = req.On < today ? req.On : today;
        decimal InterestFrom(IReadOnlyList<Instalment> plan) => plan.Where(i => i.Date > from).Sum(i => i.Interest);
        decimal PaymentAfter(IReadOnlyList<Instalment> plan) =>
            plan.FirstOrDefault(i => i.Date > req.On && i.Payment > 0)?.Payment ?? 0;
        var endBefore = before.LastOrDefault(i => i.Payment > 0)?.Date;
        var endAfter = after.LastOrDefault(i => i.Payment > 0)?.Date;
        var monthsSaved = endBefore is { } b && endAfter is { } a ? (b.Year - a.Year) * 12 + b.Month - a.Month : 0;
        var interestBefore = InterestFrom(before);
        var interestAfter = InterestFrom(after);
        return new PrepaymentSimulationDto(endBefore, endAfter, monthsSaved, PaymentAfter(before), PaymentAfter(after),
            interestBefore, interestAfter, interestBefore - interestAfter);
    }

    private static decimal? InitialRate(LoanTermsRequest req) =>
        req.InitialRatePercent ?? (req.RateType == LoanRateType.Variable && req.InitialIndexPercent is { } i
            ? i + (req.SpreadPercent ?? 0)
            : null);

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static async Task<List<Loan>> LoadAllAsync(IFinanceDb db, CancellationToken ct) =>
        await db.Loans.AsNoTracking().Include(l => l.Rates).Include(l => l.Prepayments).ToListAsync(ct);

    private static async Task<(Loan? Loan, Account? Account)> FindAsync(IFinanceDb db, Guid accountId,
        CancellationToken ct, bool tracked = false)
    {
        var query = db.Loans.Include(l => l.Rates).Include(l => l.Prepayments).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var loan = await query.FirstOrDefaultAsync(l => l.AccountId == accountId, ct);
        var account = loan is null ? null : await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        return (loan, account);
    }

    public static LoanSummaryDto Summary(Loan loan, string name, DateOnly today)
    {
        var status = LoanSchedule.StatusOn(loan.Plan(), loan.Principal, today);
        return new LoanSummaryDto(loan.AccountId, name, loan.Principal, loan.FirstPaymentOn, loan.TermMonths,
            loan.RateType, loan.RevisionMonths, loan.IndexName, loan.SpreadPercent, status.Outstanding,
            loan.Principal == 0 ? 0 : decimal.Round(1 - status.Outstanding / loan.Principal, 4),
            status.InstalmentsPaid, status.InstalmentsLeft, status.Next, status.EndDate, status.InterestPaid,
            status.InterestLeft, status.CurrentRatePercent, loan.NextRevisionAfter(today));
    }

    private static LoanDetailDto Detail(Loan loan, string name, DateOnly today) => new(
        Summary(loan, name, today),
        loan.Rates.OrderBy(r => r.EffectiveFrom)
            .Select(r => new LoanRateDto(r.Id, r.EffectiveFrom, r.AnnualRatePercent, r.IndexRatePercent)).ToList(),
        loan.Prepayments.OrderBy(p => p.On).Select(p => new LoanPrepaymentDto(p.Id, p.On, p.Amount, p.Mode)).ToList(),
        loan.Plan());
}
