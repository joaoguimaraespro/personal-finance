using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Application.Interest;
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
    DateTimeOffset UpdatedAtUtc,
    TransactionFlow Flow,
    InvestmentAssetDto? Asset,
    IReadOnlyList<SplitDto> Splits,
    // Only when the list is filtered by category and this transaction is split: the part in that category.
    decimal? CategoryAmount = null);

/// <summary>A category line of a split transaction. Amount is in the transaction's currency.</summary>
public sealed record SplitDto(Guid CategoryId, string CategoryKey, string CategoryName, decimal Amount,
    decimal BaseAmount, ExpenseNature? Nature, string? Note);

/// <summary>Request shape of a split line: the nature is the transaction's (explicit) or the category default.</summary>
public sealed record SplitLineRequest(Guid CategoryId, decimal Amount, string? Note)
{
    public SplitLine ToDomain() => new(CategoryId, Amount, Note);
}

/// <summary>Instrument details of an investment entry. Also the request shape.</summary>
public sealed record InvestmentAssetDto(
    InvestmentAssetKind Kind,
    string Symbol,
    string? Name,
    string? Isin,
    decimal? Quantity,
    decimal? UnitPrice,
    string? PriceSource)
{
    public InvestmentAsset ToDomain() => new(Kind, Symbol, Name, Isin, Quantity, UnitPrice, PriceSource);
}

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
    string? Notes,
    InvestmentAssetDto? Asset = null,
    IReadOnlyList<SplitLineRequest>? Splits = null)
{
    public TransactionDraft ToDraft() => new(Type, OccurredOn, Amount, Currency ?? SharedKernel.Currency.Base,
        AccountId, CategoryId, Nature, CounterAccountId, BucketId, GoalId, FxRate, Description, Notes,
        Asset: Asset?.ToDomain(), Splits: Splits is { Count: > 0 } ? Splits.Select(s => s.ToDomain()).ToList() : null);
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
        When(x => x.Splits is { Count: > 0 }, () =>
        {
            RuleFor(x => x.Splits!.Count).InclusiveBetween(SplitRules.MinLines, SplitRules.MaxLines)
                .OverridePropertyName("Splits")
                .WithMessage($"A split needs {SplitRules.MinLines} to {SplitRules.MaxLines} category lines.");
            RuleForEach(x => x.Splits).ChildRules(line =>
            {
                line.RuleFor(l => l.CategoryId).NotEmpty();
                line.RuleFor(l => l.Amount).GreaterThan(0).LessThan(1_000_000_000m).PrecisionScale(19, 4, true);
                line.RuleFor(l => l.Note).MaximumLength(SplitRules.MaxNoteLength);
            });
            RuleFor(x => x.Splits!.Sum(l => l.Amount)).Equal(x => x.Amount).OverridePropertyName("Splits")
                .WithMessage("The split lines must add up exactly to the amount.");
            RuleFor(x => x.Type).Must(t => t is TransactionType.Expense or TransactionType.Income)
                .WithMessage("Only expenses and income can be split by category.");
            RuleFor(x => x.CategoryId).Null().WithMessage("Give either one category or split lines, not both.");
        });
        When(x => x.Asset is not null, () =>
        {
            RuleFor(x => x.Asset!.Kind).IsInEnum();
            RuleFor(x => x.Asset!.Symbol).NotEmpty().MaximumLength(32);
            RuleFor(x => x.Asset!.Name).MaximumLength(200);
            RuleFor(x => x.Asset!.Isin).Length(12).Matches("^[A-Za-z0-9]{12}$").When(x => !string.IsNullOrEmpty(x.Asset!.Isin));
            RuleFor(x => x.Asset!.Quantity).GreaterThan(0).PrecisionScale(28, 10, true).When(x => x.Asset!.Quantity is not null);
            RuleFor(x => x.Asset!.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(28, 10, true)
                .When(x => x.Asset!.UnitPrice is not null);
            RuleFor(x => x.Asset!.PriceSource).Must(s => s is null || AssetPriceSources.All.Contains(s))
                .WithMessage("Unknown price source.");
        });
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

        group.MapPost("/", async (TransactionRequest req, IFinanceDb db, InterestAccrualService interest,
                CancellationToken ct) =>
            (await TransactionCommands.CreateAsync(db, interest, req.ToDraft(), DataSource.Manual, ct))
            .ToHttp(t => Results.Created($"/api/transactions/{t.Id}", new { t.Id })))
            .Validate<TransactionRequest>();

        group.MapPut("/{id:guid}", async (Guid id, TransactionRequest req, IFinanceDb db,
                InterestAccrualService interest, CancellationToken ct) =>
            (await TransactionCommands.UpdateAsync(db, interest, id, req.ToDraft(), ct)).ToHttp(_ => Results.NoContent()))
            .Validate<TransactionRequest>();

        group.MapDelete("/{id:guid}", async (Guid id, IFinanceDb db, TimeProvider clock,
                InterestAccrualService interest, CancellationToken ct) =>
            (await TransactionCommands.DeleteAsync(db, interest, id, clock.GetUtcNow(), ct))
            .ToHttp(_ => Results.NoContent()));

        group.MapPost("/{id:guid}/restore", async (Guid id, IFinanceDb db, InterestAccrualService interest,
                CancellationToken ct) =>
            (await TransactionCommands.RestoreAsync(db, interest, id, ct)).ToHttp(_ => Results.NoContent()));

        group.MapGet("/{id:guid}/history", async (Guid id, IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.TransactionAudits.AsNoTracking()
                .Where(a => a.TransactionId == id)
                .OrderBy(a => a.Id)
                .Select(a => new AuditEntryDto(a.Action, a.AtUtc, a.Actor, a.Changes))
                .ToListAsync(ct)));
    }

    public static bool IsBrokerSourced(DataSource source) =>
        source is DataSource.Trading212 or DataSource.InteractiveBrokers;

    /// <summary>Broker rows belong to their integration; estimated interest belongs to the accrual engine.</summary>
    public static Error? ReadOnlyError(DataSource source) => source switch
    {
        DataSource.Trading212 or DataSource.InteractiveBrokers => TransactionErrors.ReadOnlySource,
        DataSource.InterestEstimate => TransactionErrors.EstimatedInterest,
        _ => null,
    };

    private static async Task<IResult> ListAsync(
        IFinanceDb db,
        DateOnly? from,
        DateOnly? to,
        [FromQuery] TransactionType[]? type,
        TransactionFlow? flow,
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

        if (flow is { } f)
        {
            var flowTypes = TransactionTypes.Of(f).ToArray();
            query = query.Where(t => flowTypes.Contains(t.Type));
        }

        if (accountId is not null)
        {
            query = query.Where(t => t.AccountId == accountId || t.CounterAccountId == accountId);
        }

        List<Guid>? categoryIds = null;
        if (categoryId is not null)
        {
            // Selecting a parent category also includes its children; a split transaction matches when any of its
            // lines is in one of them.
            categoryIds = await db.Categories.Where(c => c.Id == categoryId || c.ParentId == categoryId)
                .Select(c => c.Id).ToListAsync(ct);
            var ids = categoryIds;
            query = query.Where(t => (t.CategoryId != null && ids.Contains(t.CategoryId.Value)) ||
                                     t.Splits.Any(s => ids.Contains(s.CategoryId)));
        }

        if (bucketId is not null)
        {
            query = query.Where(t => t.BucketId == bucketId);
        }

        if (nature is not null)
        {
            query = query.Where(t => t.Nature == nature || t.Splits.Any(s => s.Nature == nature));
        }

        if (source is not null)
        {
            query = query.Where(t => t.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => (t.Description != null && t.Description.ToLower().Contains(term)) ||
                                     (t.Notes != null && t.Notes.ToLower().Contains(term)) ||
                                     (t.AssetSymbol != null && t.AssetSymbol.ToLower().Contains(term)) ||
                                     (t.AssetName != null && t.AssetName.ToLower().Contains(term)));
        }

        var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
        var number = Math.Max(1, page ?? 1);
        var total = await query.CountAsync(ct);
        var items = await Project(query
                .OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.CreatedAtUtc)
                .Skip((number - 1) * size).Take(size), db)
            .ToListAsync(ct);
        if (categoryIds is not null)
        {
            items = items.Select(i => i.Splits.Count == 0
                ? i
                : i with { CategoryAmount = i.Splits.Where(s => categoryIds.Contains(s.CategoryId)).Sum(s => s.Amount) })
                .ToList();
        }

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
            t.Source != DataSource.Trading212 && t.Source != DataSource.InteractiveBrokers &&
            t.Source != DataSource.InterestEstimate,
            t.CreatedAtUtc, t.UpdatedAtUtc, TransactionTypes.FlowOf(t.Type),
            t.AssetKind == null || t.AssetSymbol == null
                ? null
                : new InvestmentAssetDto(t.AssetKind!.Value, t.AssetSymbol!, t.AssetName, t.AssetIsin, t.AssetQuantity,
                    t.AssetUnitPrice, t.AssetPriceSource),
            (from s in t.Splits
                join sc in db.Categories on s.CategoryId equals sc.Id
                orderby s.Position
                select new SplitDto(s.CategoryId, sc.Key, sc.Name, s.OriginalAmount, s.BaseAmount, s.Nature, s.Note))
            .ToList());
}
