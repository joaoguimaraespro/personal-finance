using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Transactions;

public sealed record TransactionDto(
    Guid Id,
    TransactionType Type,
    DateOnly OccurredOn,
    decimal Amount,
    string Currency,
    decimal FxRate,
    decimal BaseAmount,
    Guid AccountId,
    string AccountName,
    Guid? CounterAccountId,
    string? CounterAccountName,
    Guid? CategoryId,
    string? CategoryKey,
    string? CategoryName,
    ExpenseNature? Nature,
    Guid? BucketId,
    string? BucketName,
    Guid? GoalId,
    string? Description,
    string? Notes,
    DataSource Source,
    bool Editable,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TransactionRequest(
    TransactionType Type,
    DateOnly OccurredOn,
    decimal Amount,
    string? Currency,
    Guid AccountId,
    Guid? CategoryId,
    ExpenseNature? Nature,
    Guid? CounterAccountId,
    Guid? BucketId,
    Guid? GoalId,
    decimal? FxRate,
    string? Description,
    string? Notes)
{
    public TransactionDraft ToDraft() => new(Type, OccurredOn, Amount, Currency ?? SharedKernel.Currency.Base,
        AccountId, CategoryId, Nature, CounterAccountId, BucketId, GoalId, FxRate, Description, Notes);
}

public sealed class TransactionRequestValidator : AbstractValidator<TransactionRequest>
{
    public TransactionRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThan(1_000_000_000m).PrecisionScale(19, 4, true);
        RuleFor(x => x.Currency).Must(c => c is null || Currency.IsValid(c)).WithMessage("Use an ISO 4217 code.");
        RuleFor(x => x.OccurredOn).InclusiveBetween(new DateOnly(1970, 1, 1), new DateOnly(2100, 12, 31));
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.FxRate).GreaterThan(0).When(x => x.FxRate is not null);
    }
}

public sealed record TransactionPage(IReadOnlyList<TransactionDto> Items, int Total, int Page, int PageSize);

public sealed record QuickAddDefaults(Guid? AccountId, IReadOnlyList<Guid> RecentExpenseCategories,
    IReadOnlyList<Guid> RecentIncomeCategories);

public sealed record AuditEntryDto(AuditAction Action, DateTimeOffset AtUtc, string Actor, string Changes);

public static class TransactionEndpoints
{
    private const int MaxPageSize = 200;

    public static void MapTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transactions").WithTags("Transactions");

        group.MapGet("/", ListAsync);
        group.MapGet("/defaults", DefaultsAsync);
        group.MapGet("/{id:guid}", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var dto = await Project(db.Transactions.AsNoTracking().Where(t => t.Id == id), db).FirstOrDefaultAsync(ct);
            return dto is null ? ResultHttp.Problem(TransactionErrors.NotFound) : Results.Ok(dto);
        });

        group.MapPost("/", async (TransactionRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var draft = await TransactionReferences.ResolveAsync(db, req.ToDraft(), ct);
            if (draft.IsFailure)
            {
                return ResultHttp.Problem(draft.Error);
            }

            var created = Transaction.Create(draft.Value, DataSource.Manual);
            if (created.IsFailure)
            {
                return ResultHttp.Problem(created.Error);
            }

            db.Transactions.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/transactions/{created.Value.Id}", new { created.Value.Id });
        }).Validate<TransactionRequest>();

        group.MapPut("/{id:guid}", async (Guid id, TransactionRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var transaction = await db.Transactions.FindAsync([id], ct);
            if (transaction is null)
            {
                return ResultHttp.Problem(TransactionErrors.NotFound);
            }

            if (IsBrokerSourced(transaction.Source))
            {
                return ResultHttp.Problem(TransactionErrors.ReadOnlySource);
            }

            var draft = await TransactionReferences.ResolveAsync(db, req.ToDraft(), ct);
            if (draft.IsFailure)
            {
                return ResultHttp.Problem(draft.Error);
            }

            var updated = transaction.Update(draft.Value);
            if (updated.IsFailure)
            {
                return ResultHttp.Problem(updated.Error);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<TransactionRequest>();

        group.MapDelete("/{id:guid}", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var transaction = await db.Transactions.FindAsync([id], ct);
            if (transaction is null)
            {
                return ResultHttp.Problem(TransactionErrors.NotFound);
            }

            if (IsBrokerSourced(transaction.Source))
            {
                return ResultHttp.Problem(TransactionErrors.ReadOnlySource);
            }

            transaction.SoftDelete(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/restore", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var transaction = await db.Transactions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc != null, ct);
            if (transaction is null)
            {
                return ResultHttp.Problem(TransactionErrors.NotFound);
            }

            transaction.Restore();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/history", async (Guid id, IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.TransactionAudits.AsNoTracking()
                .Where(a => a.TransactionId == id)
                .OrderBy(a => a.Id)
                .Select(a => new AuditEntryDto(a.Action, a.AtUtc, a.Actor, a.Changes))
                .ToListAsync(ct)));
    }

    public static bool IsBrokerSourced(DataSource source) =>
        source is DataSource.Trading212 or DataSource.InteractiveBrokers;

    private static async Task<IResult> ListAsync(
        IFinanceDb db,
        DateOnly? from,
        DateOnly? to,
        [FromQuery] TransactionType[]? type,
        Guid? accountId,
        Guid? categoryId,
        Guid? bucketId,
        ExpenseNature? nature,
        DataSource? source,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking();
        if (from is not null)
        {
            query = query.Where(t => t.OccurredOn >= from);
        }

        if (to is not null)
        {
            query = query.Where(t => t.OccurredOn <= to);
        }

        if (type is { Length: > 0 })
        {
            query = query.Where(t => type.Contains(t.Type));
        }

        if (accountId is not null)
        {
            query = query.Where(t => t.AccountId == accountId || t.CounterAccountId == accountId);
        }

        if (categoryId is not null)
        {
            // Selecting a parent category also includes its children.
            var ids = await db.Categories.Where(c => c.Id == categoryId || c.ParentId == categoryId)
                .Select(c => c.Id).ToListAsync(ct);
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }

        if (bucketId is not null)
        {
            query = query.Where(t => t.BucketId == bucketId);
        }

        if (nature is not null)
        {
            query = query.Where(t => t.Nature == nature);
        }

        if (source is not null)
        {
            query = query.Where(t => t.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => (t.Description != null && t.Description.ToLower().Contains(term)) ||
                                     (t.Notes != null && t.Notes.ToLower().Contains(term)));
        }

        var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
        var number = Math.Max(1, page ?? 1);
        var total = await query.CountAsync(ct);
        var items = await Project(query
                .OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.CreatedAtUtc)
                .Skip((number - 1) * size).Take(size), db)
            .ToListAsync(ct);
        return Results.Ok(new TransactionPage(items, total, number, size));
    }

    private static async Task<IResult> DefaultsAsync(IFinanceDb db, CancellationToken ct)
    {
        var recent = db.Transactions.AsNoTracking().Where(t => t.Source == DataSource.Manual)
            .OrderByDescending(t => t.CreatedAtUtc).Take(200);
        var lastAccount = await recent.Where(t => t.Type == TransactionType.Expense)
            .Select(t => (Guid?)t.AccountId).FirstOrDefaultAsync(ct);

        async Task<List<Guid>> TopCategories(TransactionType type) => await recent
            .Where(t => t.Type == type && t.CategoryId != null)
            .GroupBy(t => t.CategoryId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(6)
            .ToListAsync(ct);

        return Results.Ok(new QuickAddDefaults(lastAccount, await TopCategories(TransactionType.Expense),
            await TopCategories(TransactionType.Income)));
    }

    internal static IQueryable<TransactionDto> Project(IQueryable<Transaction> query, IFinanceDb db) =>
        from t in query
        join a in db.Accounts on t.AccountId equals a.Id
        join ca in db.Accounts on t.CounterAccountId equals ca.Id into cas
        from ca in cas.DefaultIfEmpty()
        join c in db.Categories on t.CategoryId equals c.Id into cs
        from c in cs.DefaultIfEmpty()
        join b in db.Buckets on t.BucketId equals b.Id into bs
        from b in bs.DefaultIfEmpty()
        select new TransactionDto(t.Id, t.Type, t.OccurredOn, t.OriginalAmount, t.OriginalCurrency, t.FxRate,
            t.BaseAmount, t.AccountId, a.Name, t.CounterAccountId, ca == null ? null : ca.Name, t.CategoryId,
            c == null ? null : c.Key, c == null ? null : c.Name, t.Nature, t.BucketId, b == null ? null : b.Name,
            t.GoalId, t.Description, t.Notes, t.Source,
            t.Source != DataSource.Trading212 && t.Source != DataSource.InteractiveBrokers,
            t.CreatedAtUtc, t.UpdatedAtUtc);
}
