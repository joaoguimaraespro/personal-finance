using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Http;
using Finance.Domain.Interest;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Interest;

public sealed record InterestRateDto(Guid Id, DateOnly EffectiveFrom, decimal AnnualRatePercent,
    decimal WithholdingPercent);

public sealed record AddInterestRateRequest(decimal AnnualRatePercent, DateOnly EffectiveFrom,
    decimal? WithholdingPercent);

public sealed class AddInterestRateValidator : AbstractValidator<AddInterestRateRequest>
{
    public AddInterestRateValidator()
    {
        RuleFor(x => x.AnnualRatePercent).InclusiveBetween(0, 100).PrecisionScale(9, 4, true);
        RuleFor(x => x.WithholdingPercent).InclusiveBetween(0, 100).PrecisionScale(9, 4, true)
            .When(x => x.WithholdingPercent is not null);
        RuleFor(x => x.EffectiveFrom).InclusiveBetween(new DateOnly(1970, 1, 1), new DateOnly(2100, 12, 31));
    }
}

/// <summary>A closed month whose estimated interest waits for the user to confirm or correct it.</summary>
public sealed record InterestMonthDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string? Institution,
    string Month,
    string Currency,
    decimal EstimatedAmount,
    decimal EstimatedGross,
    InterestMonthStatus Status,
    decimal? ActualAmount);

/// <summary>Without an amount the estimate is confirmed as is; with one, the real amount replaces it (0 = not paid).</summary>
public sealed record ReconcileInterestRequest(decimal? Amount, DateOnly? OccurredOn, decimal? FxRate);

public sealed class ReconcileInterestValidator : AbstractValidator<ReconcileInterestRequest>
{
    public ReconcileInterestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).LessThan(1_000_000_000m).PrecisionScale(19, 4, true)
            .When(x => x.Amount is not null);
        RuleFor(x => x.FxRate).GreaterThan(0).When(x => x.FxRate is not null);
    }
}

public static class InterestEndpoints
{
    public static void MapInterest(this IEndpointRouteBuilder app)
    {
        var rates = app.MapGroup("/accounts/{accountId:guid}/interest-rates").WithTags("Interest");

        rates.MapGet("/", async (Guid accountId, IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.InterestRates.AsNoTracking()
                .Where(r => r.AccountId == accountId)
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new InterestRateDto(r.Id, r.EffectiveFrom, r.AnnualRatePercent, r.WithholdingPercent))
                .ToListAsync(ct)));

        rates.MapPost("/", async (Guid accountId, AddInterestRateRequest req, IFinanceDb db,
            InterestAccrualService interest, TimeProvider clock, CancellationToken ct) =>
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                return ResultHttp.Problem(AccountEndpoints.NotFound);
            }

            if (await db.InterestRates.AnyAsync(r => r.AccountId == accountId && r.EffectiveFrom == req.EffectiveFrom, ct))
            {
                return ResultHttp.Problem(InterestErrors.DuplicatePeriod);
            }

            var created = AccountInterestRate.Create(account, req.EffectiveFrom, req.AnnualRatePercent,
                req.WithholdingPercent, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return ResultHttp.Problem(created.Error);
            }

            db.InterestRates.Add(created.Value);
            await db.SaveChangesAsync(ct);
            await interest.TryRecalculateAsync([accountId], ct);
            return Results.Created($"/api/accounts/{accountId}/interest-rates/{created.Value.Id}",
                new { created.Value.Id });
        }).Validate<AddInterestRateRequest>();

        // Undo for a mistyped change: only the latest period can go, earlier history is never rewritten.
        rates.MapDelete("/{id:guid}", async (Guid accountId, Guid id, IFinanceDb db, InterestAccrualService interest,
            CancellationToken ct) =>
        {
            var periods = await db.InterestRates.Where(r => r.AccountId == accountId)
                .OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct);
            var rate = periods.FirstOrDefault(r => r.Id == id);
            if (rate is null)
            {
                return ResultHttp.Problem(InterestErrors.RateNotFound);
            }

            if (periods[0].Id != id)
            {
                return ResultHttp.Problem(InterestErrors.OnlyLatestRemovable);
            }

            db.InterestRates.Remove(rate);
            await db.SaveChangesAsync(ct);
            await interest.TryRecalculateAsync([accountId], ct);
            return Results.NoContent();
        });

        var months = app.MapGroup("/interest").WithTags("Interest");

        // Closed months still carrying an estimate: "Revolut paid X € interest in September?"
        months.MapGet("/pending", async (IFinanceDb db, InterestAccrualService interest, CancellationToken ct) =>
        {
            var current = YearMonth.From(interest.Today);
            var rows = await Query(db).Where(x => x.M.Status == InterestMonthStatus.Estimated &&
                                                  (x.M.Year < current.Year ||
                                                   (x.M.Year == current.Year && x.M.Month < current.Month)))
                .OrderBy(x => x.M.Year).ThenBy(x => x.M.Month).ThenBy(x => x.A.Name)
                .Take(200)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(ToDto));
        });

        months.MapGet("/accounts/{accountId:guid}", async (Guid accountId, IFinanceDb db, CancellationToken ct) =>
        {
            var rows = await Query(db).Where(x => x.M.AccountId == accountId)
                .OrderByDescending(x => x.M.Year).ThenByDescending(x => x.M.Month)
                .Take(120)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(ToDto));
        });

        months.MapPost("/{id:guid}/reconcile", ReconcileAsync).Validate<ReconcileInterestRequest>();
    }

    private static async Task<IResult> ReconcileAsync(Guid id, ReconcileInterestRequest req, IFinanceDb db,
        InterestAccrualService interest, TimeProvider clock, CancellationToken ct)
    {
        var month = await db.InterestMonths.FindAsync([id], ct);
        if (month is null)
        {
            return ResultHttp.Problem(InterestErrors.MonthNotFound);
        }

        if (month.IsResolved)
        {
            return ResultHttp.Problem(InterestErrors.AlreadyResolved);
        }

        if (month.Period >= YearMonth.From(interest.Today))
        {
            return ResultHttp.Problem(InterestErrors.MonthOpen);
        }

        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == month.AccountId, ct);
        var amount = req.Amount ?? month.EstimatedAmount;

        // The estimate leaves the ledger in the same save that books the real figure: never both, never neither.
        var estimates = await db.Transactions
            .Where(t => t.Source == DataSource.InterestEstimate && t.AccountId == month.AccountId &&
                        t.ExternalId == InterestAccrualService.ExternalIdFor(month.AccountId, month.Period))
            .ToListAsync(ct);
        db.Transactions.RemoveRange(estimates);

        Guid? realId = null;
        if (amount > 0)
        {
            var fxRate = account.Currency == Currency.Base
                ? null
                : req.FxRate ?? await interest.LatestFxRateAsync(account.Currency, ct);
            if (account.Currency != Currency.Base && fxRate is null)
            {
                return ResultHttp.Problem(InterestErrors.FxRateRequired);
            }

            var occurredOn = req.OccurredOn is { } on && month.Period.Contains(on) ? on : month.Period.LastDay;
            var draft = new TransactionDraft(TransactionType.Income, occurredOn, amount, account.Currency, account.Id,
                InterestAccrualService.InterestCategoryId, FxRate: fxRate, Description: $"Interest {month.Period}");
            var created = Transaction.Create(draft, DataSource.Manual);
            if (created.IsFailure)
            {
                return ResultHttp.Problem(created.Error);
            }

            db.Transactions.Add(created.Value);
            realId = created.Value.Id;
        }

        month.Resolve(amount, realId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        // Later months earn interest on a slightly different balance now.
        await interest.TryRecalculateAsync([month.AccountId], ct);
        return Results.Ok(new { TransactionId = realId, month.Status });
    }

    private static IQueryable<MonthRow> Query(IFinanceDb db) =>
        from m in db.InterestMonths.AsNoTracking()
        join a in db.Accounts.AsNoTracking() on m.AccountId equals a.Id
        select new MonthRow { M = m, A = a };

    private static InterestMonthDto ToDto(MonthRow x) => new(x.M.Id, x.A.Id, x.A.Name, x.A.Institution,
        x.M.Period.ToString(), x.A.Currency, x.M.EstimatedAmount, x.M.EstimatedGross, x.M.Status, x.M.ActualAmount);

    // Initializer (not constructor) projection so EF can keep composing filters on it.
    private sealed class MonthRow
    {
        public required InterestMonth M { get; init; }
        public required Finance.Domain.Accounts.Account A { get; init; }
    }
}
