using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Application.Interest;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Accounts;

public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountKind Kind,
    string Currency,
    string? Institution,
    string? IdentifierMasked,
    decimal OpeningBalance,
    DateOnly OpeningBalanceOn,
    decimal Balance,
    bool IsManual,
    bool IsLiability,
    bool Archived,
    AccountInterestDto? Interest = null);

/// <summary>
/// Interest picture of a savings/bank account. <see cref="EstimatedInBalance"/> is the part of <c>Balance</c> that is
/// still an estimate (not yet confirmed against what the bank paid).
/// </summary>
public sealed record AccountInterestDto(
    decimal? AnnualRatePercent,
    decimal? WithholdingPercent,
    DateOnly? RateEffectiveFrom,
    InterestPayout Payout,
    decimal YearToDate,
    decimal YearToDateEstimated,
    decimal EstimatedInBalance);

public sealed record CreateAccountRequest(
    string Name,
    AccountKind Kind,
    string Currency,
    decimal OpeningBalance,
    DateOnly? OpeningBalanceOn,
    string? Institution,
    string? Identifier,
    InterestPayout? InterestPayout = null);

public sealed record UpdateAccountRequest(
    string Name,
    decimal OpeningBalance,
    DateOnly OpeningBalanceOn,
    string? Institution,
    string? Identifier,
    InterestPayout? InterestPayout = null);

public sealed class CreateAccountValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).IsInEnum()
            .NotEqual(AccountKind.Broker).WithMessage("Broker accounts are created by broker connections.");
        RuleFor(x => x.Currency).Must(Currency.IsValid).WithMessage("Use an ISO 4217 currency code.");
        RuleFor(x => x.Institution).MaximumLength(80);
        RuleFor(x => x.Identifier).MaximumLength(64);
        RuleFor(x => x.InterestPayout).IsInEnum().When(x => x.InterestPayout is not null);
    }
}

public sealed class UpdateAccountValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Institution).MaximumLength(80);
        RuleFor(x => x.Identifier).MaximumLength(64);
        RuleFor(x => x.InterestPayout).IsInEnum().When(x => x.InterestPayout is not null);
    }
}

public static class AccountEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Account.NotFound", "Account not found.");

    public static readonly Error ReadOnly =
        Error.Forbidden("Account.ReadOnly", "Broker accounts are read-only and managed by their integration.");

    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapGet("/", async (IFinanceDb db, bool? includeArchived, TimeProvider clock, CancellationToken ct) =>
        {
            var accounts = await db.Accounts.AsNoTracking()
                .Where(a => includeArchived == true || a.ArchivedAtUtc == null)
                .OrderBy(a => a.Kind).ThenBy(a => a.Name)
                .ToListAsync(ct);
            var balances = await AccountBalances.ComputeAsync(db, accounts, null, ct);
            var interest = await InterestSummariesAsync(db, accounts,
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct);
            return Results.Ok(accounts.Select(a => ToDto(a, balances[a.Id], interest.GetValueOrDefault(a.Id))));
        });

        group.MapPost("/", async (CreateAccountRequest req, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var account = Account.Create(req.Name, req.Kind, req.Currency, req.OpeningBalance,
                req.OpeningBalanceOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), req.Institution,
                req.Identifier);
            if (req.InterestPayout is { } payout && account.SupportsInterest)
            {
                account.SetInterestPayout(payout);
            }

            db.Accounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/accounts/{account.Id}", ToDto(account, account.OpeningBalance));
        }).Validate<CreateAccountRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateAccountRequest req, IFinanceDb db,
            InterestAccrualService interest, CancellationToken ct) =>
        {
            var account = await db.Accounts.FindAsync([id], ct);
            if (account is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (!account.IsManual)
            {
                return ResultHttp.Problem(ReadOnly);
            }

            // An empty identifier in the request keeps the stored one; "-" clears it.
            var identifier = req.Identifier switch
            {
                null or "" => account.Identifier,
                "-" => null,
                _ => req.Identifier,
            };
            account.Update(req.Name, req.OpeningBalance, req.OpeningBalanceOn, req.Institution, identifier);
            if (req.InterestPayout is { } payout && account.SupportsInterest)
            {
                account.SetInterestPayout(payout);
            }

            await db.SaveChangesAsync(ct);
            // Opening balance, its date and the payout frequency all move the daily balances.
            await interest.TryRecalculateAsync([id], ct);
            return Results.NoContent();
        }).Validate<UpdateAccountRequest>();

        group.MapPost("/{id:guid}/archive", (Guid id, IFinanceDb db, TimeProvider clock,
            InterestAccrualService interest, CancellationToken ct) =>
            SetArchived(id, archived: true, db, clock, interest, ct));

        group.MapPost("/{id:guid}/restore", (Guid id, IFinanceDb db, TimeProvider clock,
            InterestAccrualService interest, CancellationToken ct) =>
            SetArchived(id, archived: false, db, clock, interest, ct));
    }

    private static async Task<IResult> SetArchived(Guid id, bool archived, IFinanceDb db, TimeProvider clock,
        InterestAccrualService interest, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([id], ct);
        if (account is null)
        {
            return ResultHttp.Problem(NotFound);
        }

        if (archived)
        {
            account.Archive(clock.GetUtcNow());
        }
        else
        {
            account.Restore();
        }

        await db.SaveChangesAsync(ct);
        // Archived accounts stop accruing from the archive date; restoring resumes it.
        await interest.TryRecalculateAsync([id], ct);
        return Results.NoContent();
    }

    public static string? Mask(string? identifier) => identifier switch
    {
        null or "" => null,
        { Length: <= 4 } => "••••",
        _ => "••••" + identifier[^4..],
    };

    private static AccountDto ToDto(Account a, decimal balance, AccountInterestDto? interest = null) => new(a.Id,
        a.Name, a.Kind, a.Currency, a.Institution, Mask(a.Identifier), a.OpeningBalance, a.OpeningBalanceOn, balance,
        a.IsManual, a.IsLiability, a.ArchivedAtUtc is not null,
        interest ?? (a.SupportsInterest ? new AccountInterestDto(null, null, null, a.InterestPayout, 0, 0, 0) : null));

    /// <summary>Current rate and this year's interest (real + estimated) for every account that can earn interest.</summary>
    public static async Task<Dictionary<Guid, AccountInterestDto>> InterestSummariesAsync(IFinanceDb db,
        IReadOnlyCollection<Account> accounts, DateOnly today, CancellationToken ct)
    {
        var ids = accounts.Where(a => a.SupportsInterest).Select(a => a.Id).ToList();
        var rates = await db.InterestRates.AsNoTracking()
            .Where(r => ids.Contains(r.AccountId) && r.EffectiveFrom <= today)
            .ToListAsync(ct);
        var interestCategory = InterestAccrualService.InterestCategoryId;
        var yearStart = new DateOnly(today.Year, 1, 1);
        var sums = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId) && t.Type == TransactionType.Income &&
                        t.CategoryId == interestCategory &&
                        (t.OccurredOn >= yearStart || t.Source == DataSource.InterestEstimate))
            .GroupBy(t => new
            {
                t.AccountId,
                Estimated = t.Source == DataSource.InterestEstimate,
                ThisYear = t.OccurredOn >= yearStart,
            })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Estimated,
                g.Key.ThisYear,
                Base = g.Sum(t => t.BaseAmount),
                Original = g.Sum(t => t.OriginalAmount),
            })
            .ToListAsync(ct);
        // Interest booked as one line of a split income counts too (split rows are never estimates).
        var splitLines = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId) && t.Type == TransactionType.Income && t.OccurredOn >= yearStart)
            .SelectMany(t => t.Splits.Where(s => s.CategoryId == interestCategory),
                (t, s) => new { t.AccountId, s.BaseAmount, s.OriginalAmount })
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Base = g.Sum(x => x.BaseAmount), Original = g.Sum(x => x.OriginalAmount) })
            .ToListAsync(ct);
        sums.AddRange(splitLines.Select(l => new
        {
            l.AccountId, Estimated = false, ThisYear = true, l.Base, l.Original,
        }));

        var result = new Dictionary<Guid, AccountInterestDto>();
        foreach (var account in accounts.Where(a => a.SupportsInterest))
        {
            var current = rates.Where(r => r.AccountId == account.Id).MaxBy(r => r.EffectiveFrom);
            var mine = sums.Where(s => s.AccountId == account.Id)
                .Select(s => new
                {
                    s.Estimated,
                    s.ThisYear,
                    Amount = account.Currency == Currency.Base ? s.Base : s.Original,
                })
                .ToList();
            result[account.Id] = new AccountInterestDto(current?.AnnualRatePercent, current?.WithholdingPercent,
                current?.EffectiveFrom, account.InterestPayout,
                mine.Where(s => s.ThisYear).Sum(s => s.Amount),
                mine.Where(s => s.ThisYear && s.Estimated).Sum(s => s.Amount),
                mine.Where(s => s.Estimated).Sum(s => s.Amount));
        }

        return result;
    }
}
