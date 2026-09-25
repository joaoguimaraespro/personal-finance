using System.Security.Cryptography;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Imports;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Imports.Application.FinanceTracker;

public sealed record ImportSummaryDto(Guid Id, string Kind, string FileName, ImportStatus Status, int Created,
    int Skipped, DateTimeOffset CreatedAtUtc, DateTimeOffset? CommittedAtUtc, DateTimeOffset? RolledBackAtUtc);

public sealed record ImportPreviewDto(Guid Id, ImportStatus Status, string FileName, ImportPreview Preview,
    IReadOnlyList<string> WorkbookCategories);

public sealed record MappingRequest(int Year, Guid? MainAccountId, Guid? InvestmentAccountId, Guid? SavingsAccountId,
    Dictionary<string, Guid>? Categories, bool? ImportBudget);

public sealed record CommitResult(int Created, int Skipped, bool BudgetCreated, int Checks);

/// <summary>Analyze → Map → Validate → Preview → Import → Undo for the original Finance Tracker workbook.</summary>
public static class ImportEndpoints
{
    public const string Kind = "finance-tracker-xlsx";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Error NotFound = Error.NotFound("Import.NotFound", "Import not found.");

    private sealed record Payload(RawWorkbook Workbook, ImportPreview Preview);

    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/imports").WithTags("Imports");

        group.MapGet("/", async (IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.Imports.AsNoTracking().OrderByDescending(i => i.CreatedAtUtc).Take(100)
                .Select(i => new ImportSummaryDto(i.Id, i.Kind, i.FileName, i.Status, i.Created, i.Skipped,
                    i.CreatedAtUtc, i.CommittedAtUtc, i.RolledBackAtUtc))
                .ToListAsync(ct)));

        // Step 1 — Analyze: parse, suggest a mapping and return a first preview. Nothing is written to the ledger.
        group.MapPost("/finance-tracker", async (IFormFile file, int? year, IFinanceDb db, TimeProvider clock,
            CancellationToken ct) =>
        {
            if (file.Length is 0 or > WorkbookGuard.MaxFileBytes)
            {
                return ResultHttp.Problem(Error.Validation("Import.File", "Upload an .xlsx file up to 10 MB."));
            }

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            var sha = Convert.ToHexStringLower(SHA256.HashData(buffer));
            buffer.Position = 0;

            var (workbook, error) = FinanceTrackerReader.Read(buffer);
            if (workbook is null)
            {
                return ResultHttp.Problem(Error.Validation("Import.Layout", error!));
            }

            var mainAccount = await db.Accounts.Where(a => a.ArchivedAtUtc == null && a.Kind != Finance.Domain.Accounts.AccountKind.Broker)
                .OrderBy(a => a.Kind).ThenBy(a => a.CreatedAtUtc).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            var mapping = new ImportMapping(year ?? clock.GetUtcNow().Year, mainAccount, null, null,
                ImportPlanner.SuggestCategories(workbook));
            var preview = ImportPlanner.Plan(workbook, mapping);

            var batch = ImportBatch.Start(Kind, DataSource.Xlsx, SafeFileName(file.FileName), sha,
                JsonSerializer.Serialize(new Payload(workbook, preview), Json), clock.GetUtcNow());
            db.Imports.Add(batch);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(batch, workbook, preview));
        }).DisableAntiforgery().Accepts<IFormFile>("multipart/form-data");

        group.MapGet("/{id:guid}", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var batch = await db.Imports.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            if (batch is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            var payload = Read(batch);
            return Results.Ok(ToDto(batch, payload.Workbook, payload.Preview));
        });

        // Steps 2–4 — Map, Validate, Preview: re-plan with the user's mapping.
        group.MapPut("/{id:guid}/mapping", async (Guid id, MappingRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var batch = await db.Imports.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (batch is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (batch.Status != ImportStatus.Previewed)
            {
                return ResultHttp.Problem(Error.Conflict("Import.State", "Only a previewed import can be re-mapped."));
            }

            var payload = Read(batch);
            var accountIds = new[] { req.MainAccountId, req.InvestmentAccountId, req.SavingsAccountId }
                .Where(a => a is not null).Select(a => a!.Value).ToList();
            var validAccounts = await db.Accounts.CountAsync(a => accountIds.Contains(a.Id) && a.ArchivedAtUtc == null, ct);
            if (validAccounts != accountIds.Distinct().Count())
            {
                return ResultHttp.Problem(Error.Validation("Import.Account", "Unknown or archived account."));
            }

            var categories = new Dictionary<string, Guid>(payload.Preview.Mapping.Categories);
            foreach (var (label, categoryId) in req.Categories ?? [])
            {
                categories[FinanceTrackerReader.Normalize(label)] = categoryId;
            }

            var categoryIds = categories.Values.Distinct().ToList();
            var knownExpenseCategories = await db.Categories
                .CountAsync(c => categoryIds.Contains(c.Id) && c.Type == Finance.Domain.Categories.CategoryType.Expense, ct);
            if (knownExpenseCategories != categoryIds.Count)
            {
                return ResultHttp.Problem(Error.Validation("Import.Category", "Map labels to expense categories only."));
            }

            var mapping = new ImportMapping(req.Year, req.MainAccountId, req.InvestmentAccountId, req.SavingsAccountId,
                categories, req.ImportBudget ?? true);
            var preview = ImportPlanner.Plan(payload.Workbook, mapping);
            batch.UpdatePayload(JsonSerializer.Serialize(payload with { Preview = preview }, Json));
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(batch, payload.Workbook, preview));
        });

        // Step 5 — Import: one database transaction, idempotent per source cell.
        group.MapPost("/{id:guid}/commit", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var batch = await db.Imports.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (batch is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (batch.Status != ImportStatus.Previewed)
            {
                return ResultHttp.Problem(Error.Conflict("Import.State", "This import was already committed."));
            }

            var preview = Read(batch).Preview;
            if (!preview.CanCommit)
            {
                return ResultHttp.Problem(Error.Validation("Import.Invalid", string.Join(" ", preview.Errors)));
            }

            return Results.Ok(await CommitAsync(db, batch, preview, clock, ct));
        });

        // Step 6 — Undo: removes exactly the rows this import created. Each removal is audited.
        group.MapPost("/{id:guid}/undo", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var batch = await db.Imports.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (batch is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (batch.Status != ImportStatus.Committed)
            {
                return ResultHttp.Problem(Error.Conflict("Import.State", "Only committed imports can be undone."));
            }

            var rows = await db.Transactions.IgnoreQueryFilters().Where(t => t.ImportId == id).ToListAsync(ct);
            db.Transactions.RemoveRange(rows);
            batch.MarkRolledBack(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Removed = rows.Count });
        });

        return app;
    }

    public static async Task<CommitResult> CommitAsync(IFinanceDb db, ImportBatch batch, ImportPreview preview,
        TimeProvider clock, CancellationToken ct)
    {
        var externalIds = preview.Transactions.Select(t => t.ExternalId).ToList();
        var existing = (await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.Source == DataSource.Xlsx && t.ExternalId != null && externalIds.Contains(t.ExternalId))
            .Select(t => t.ExternalId!).ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var p in preview.Transactions.Where(p => !existing.Contains(p.ExternalId)))
        {
            var draft = new TransactionDraft(p.Type, p.OccurredOn, p.Amount, Currency.Base, p.AccountId, p.CategoryId,
                p.Nature, p.CounterAccountId, p.BucketId, Description: p.Description, Notes: p.Notes);
            var result = Transaction.Create(draft, DataSource.Xlsx, batch.Id, p.ExternalId);
            if (result.IsSuccess)
            {
                db.Transactions.Add(result.Value);
                created++;
            }
        }

        var year = preview.Mapping.Year;
        var budgetCreated = false;
        if (preview.Budget is { } b &&
            !await db.Budgets.AnyAsync(x => x.EffectiveFrom == new YearMonth(year, 1).FirstDay, ct))
        {
            var budget = Budget.Create(new YearMonth(year, 1),
            [
                new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, b.Stocks,
                    Finance.Domain.SystemCatalog.BucketId("stocks-etfs")),
                new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, b.Crypto,
                    Finance.Domain.SystemCatalog.BucketId("crypto")),
                new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, b.Travel,
                    Finance.Domain.SystemCatalog.BucketId("travel-fund")),
                new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, b.OtherSavings,
                    Finance.Domain.SystemCatalog.BucketId("other-savings")),
                new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.Remainder, 0),
            ], $"Imported from {batch.FileName}");
            if (budget.IsSuccess)
            {
                db.Budgets.Add(budget.Value);
                budgetCreated = true;
            }
        }

        foreach (var check in preview.Checks)
        {
            var existingCheck = await db.AllocationChecks.FirstOrDefaultAsync(
                x => x.Year == year && x.Month == check.Month && x.BucketId == check.BucketId, ct);
            if (existingCheck is null)
            {
                db.AllocationChecks.Add(AllocationCheck.Create(new YearMonth(year, check.Month), check.BucketId,
                    check.Status));
            }
        }

        batch.MarkCommitted(created, preview.Transactions.Count - created, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new CommitResult(created, preview.Transactions.Count - created, budgetCreated, preview.Checks.Count);
    }

    private static Payload Read(ImportBatch batch) => JsonSerializer.Deserialize<Payload>(batch.Payload, Json)!;

    private static ImportPreviewDto ToDto(ImportBatch batch, RawWorkbook workbook, ImportPreview preview) => new(
        batch.Id, batch.Status, batch.FileName, preview,
        workbook.Rows.Where(r => r.Block is RawBlock.Fixed or RawBlock.Variable && r.CategoryLabel is not null)
            .Select(r => r.CategoryLabel!).Distinct().Order().ToList());

    private static string SafeFileName(string name)
    {
        var clean = new string(Path.GetFileName(name).Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ').ToArray());
        return clean.Length is 0 ? "workbook.xlsx" : clean[..Math.Min(clean.Length, 120)];
    }
}
