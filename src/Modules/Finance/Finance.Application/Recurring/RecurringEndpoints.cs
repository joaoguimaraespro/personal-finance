using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Application.Transactions;
using Finance.Domain.Categories;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Recurring;

public sealed record RecurringDto(
    Guid Id,
    string Name,
    TransactionType Type,
    decimal Amount,
    string Currency,
    Guid AccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    ExpenseNature? Nature,
    Guid? BucketId,
    string? Description,
    RecurrenceFrequency Frequency,
    int Interval,
    int? DayOfMonth,
    DateOnly StartOn,
    DateOnly? EndOn,
    DateOnly NextDueOn,
    bool IsActive);

public sealed record RecurringRequest(
    string Name,
    TransactionType Type,
    decimal Amount,
    string? Currency,
    Guid AccountId,
    RecurrenceFrequency Frequency,
    DateOnly StartOn,
    int? Interval,
    int? DayOfMonth,
    DateOnly? EndOn,
    Guid? CategoryId,
    ExpenseNature? Nature,
    Guid? CounterAccountId,
    Guid? BucketId,
    string? Description)
{
    public RecurringDefinition ToDefinition() => new(Name, Type, Amount, Currency ?? SharedKernel.Currency.Base,
        AccountId, Frequency, StartOn, Interval ?? 1, DayOfMonth, EndOn, CategoryId, Nature, CounterAccountId,
        BucketId, Description);
}

public sealed class RecurringRequestValidator : AbstractValidator<RecurringRequest>
{
    public RecurringRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.Interval).InclusiveBetween(1, 24).When(x => x.Interval is not null);
        RuleFor(x => x.DayOfMonth).InclusiveBetween(1, 31).When(x => x.DayOfMonth is not null);
        RuleFor(x => x.EndOn).GreaterThanOrEqualTo(x => x.StartOn).When(x => x.EndOn is not null);
        RuleFor(x => x.Currency).Must(c => c is null || Currency.IsValid(c));
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

public sealed record ExpectedDto(
    Guid Id,
    Guid RecurringTransactionId,
    string Name,
    TransactionType Type,
    DateOnly DueOn,
    decimal Amount,
    string Currency,
    ExpectedStatus Status,
    Guid? TransactionId);

/// <summary>Optional overrides when confirming: the real amount/date often differ slightly from the template.</summary>
public sealed record ConfirmExpectedRequest(decimal? Amount, DateOnly? OccurredOn, Guid? AccountId, decimal? FxRate,
    string? Notes);

public static class RecurringEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Recurring.NotFound", "Recurring transaction not found.");
    public static readonly Error ExpectedNotFound = Error.NotFound("Expected.NotFound", "Expected transaction not found.");
    public static readonly Error AlreadyResolved = Error.Conflict("Expected.Resolved", "Already confirmed or skipped.");

    public static void MapRecurring(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/recurring").WithTags("Recurring");

        group.MapGet("/", async (IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.RecurringTransactions.AsNoTracking().OrderBy(r => r.NextDueOn)
                .Select(r => new RecurringDto(r.Id, r.Name, r.Type, r.Amount, r.Currency, r.AccountId,
                    r.CounterAccountId, r.CategoryId, r.Nature, r.BucketId, r.Description, r.Frequency, r.Interval,
                    r.DayOfMonth, r.StartOn, r.EndOn, r.NextDueOn, r.IsActive))
                .ToListAsync(ct)));

        group.MapPost("/", async (RecurringRequest req, IFinanceDb db, RecurringProposer proposer,
            CancellationToken ct) =>
        {
            var definition = req.ToDefinition();
            var check = await ValidateReferences(db, definition, ct);
            if (check is not null)
            {
                return ResultHttp.Problem(check);
            }

            var recurring = RecurringTransaction.Create(definition);
            db.RecurringTransactions.Add(recurring);
            await db.SaveChangesAsync(ct);
            await proposer.ProposeDueAsync(ct);
            return Results.Created($"/api/recurring/{recurring.Id}", new { recurring.Id });
        }).Validate<RecurringRequest>();

        group.MapPut("/{id:guid}", async (Guid id, RecurringRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var recurring = await db.RecurringTransactions.FindAsync([id], ct);
            if (recurring is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            var definition = req.ToDefinition();
            var check = await ValidateReferences(db, definition, ct);
            if (check is not null)
            {
                return ResultHttp.Problem(check);
            }

            recurring.Update(definition);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<RecurringRequest>();

        group.MapPost("/{id:guid}/pause", (Guid id, IFinanceDb db, CancellationToken ct) => SetActive(id, false, db, ct));
        group.MapPost("/{id:guid}/resume", (Guid id, IFinanceDb db, CancellationToken ct) => SetActive(id, true, db, ct));

        group.MapDelete("/{id:guid}", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var recurring = await db.RecurringTransactions.FindAsync([id], ct);
            if (recurring is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            // Pending proposals go with the template; confirmed ones are already real transactions.
            await db.ExpectedTransactions
                .Where(e => e.RecurringTransactionId == id && e.Status == ExpectedStatus.Pending)
                .ExecuteDeleteAsync(ct);
            db.RecurringTransactions.Remove(recurring);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var expected = app.MapGroup("/expected").WithTags("Recurring");

        expected.MapGet("/", async (IFinanceDb db, ExpectedStatus? status, CancellationToken ct) =>
            Results.Ok(await (
                    from e in db.ExpectedTransactions.AsNoTracking()
                    join r in db.RecurringTransactions on e.RecurringTransactionId equals r.Id
                    where e.Status == (status ?? ExpectedStatus.Pending)
                    orderby e.DueOn
                    select new ExpectedDto(e.Id, r.Id, r.Name, r.Type, e.DueOn, e.Amount, e.Currency, e.Status,
                        e.TransactionId))
                .Take(200)
                .ToListAsync(ct)));

        expected.MapPost("/{id:guid}/confirm", ConfirmAsync);

        expected.MapPost("/{id:guid}/skip", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var item = await db.ExpectedTransactions.FindAsync([id], ct);
            if (item is null)
            {
                return ResultHttp.Problem(ExpectedNotFound);
            }

            if (item.Status != ExpectedStatus.Pending)
            {
                return ResultHttp.Problem(AlreadyResolved);
            }

            item.Skip(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> ConfirmAsync(Guid id, ConfirmExpectedRequest? req, IFinanceDb db,
        TimeProvider clock, CancellationToken ct)
    {
        var item = await db.ExpectedTransactions.FindAsync([id], ct);
        if (item is null)
        {
            return ResultHttp.Problem(ExpectedNotFound);
        }

        if (item.Status != ExpectedStatus.Pending)
        {
            return ResultHttp.Problem(AlreadyResolved);
        }

        var template = await db.RecurringTransactions.AsNoTracking()
            .FirstAsync(r => r.Id == item.RecurringTransactionId, ct);
        var draft = template.ToDraft(req?.OccurredOn ?? item.DueOn, req?.Amount ?? item.Amount) with
        {
            AccountId = req?.AccountId ?? template.AccountId,
            FxRate = req?.FxRate,
            Notes = req?.Notes,
        };

        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct);
        if (resolved.IsFailure)
        {
            return ResultHttp.Problem(resolved.Error);
        }

        var created = Transaction.Create(resolved.Value, DataSource.Recurring, expectedTransactionId: item.Id);
        if (created.IsFailure)
        {
            return ResultHttp.Problem(created.Error);
        }

        db.Transactions.Add(created.Value);
        item.Confirm(created.Value.Id, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { TransactionId = created.Value.Id });
    }

    private static async Task<IResult> SetActive(Guid id, bool active, IFinanceDb db, CancellationToken ct)
    {
        var recurring = await db.RecurringTransactions.FindAsync([id], ct);
        if (recurring is null)
        {
            return ResultHttp.Problem(NotFound);
        }

        recurring.SetActive(active);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<Error?> ValidateReferences(IFinanceDb db, RecurringDefinition def, CancellationToken ct)
    {
        var probe = new TransactionDraft(def.Type, def.StartOn, def.Amount, def.Currency, def.AccountId,
            def.CategoryId, def.Nature, def.CounterAccountId, def.BucketId, FxRate: def.Currency == Currency.Base ? null : 1m);
        var resolved = await TransactionReferences.ResolveAsync(db, probe, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var probeResult = Transaction.Create(resolved.Value, DataSource.Recurring);
        return probeResult.IsFailure ? probeResult.Error : null;
    }
}
