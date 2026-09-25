using Finance.Domain;
using Finance.Domain.Allocation;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using SharedKernel;

namespace Imports.Application.FinanceTracker;

/// <summary>User choices in the "Map" step.</summary>
public sealed record ImportMapping(
    int Year,
    Guid? MainAccountId,
    Guid? InvestmentAccountId,
    Guid? SavingsAccountId,
    IReadOnlyDictionary<string, Guid> Categories,
    bool ImportBudget = true);

public sealed record PlannedTransaction(
    string ExternalId,
    int Month,
    TransactionType Type,
    DateOnly OccurredOn,
    decimal Amount,
    Guid? CategoryId,
    ExpenseNature? Nature,
    Guid? BucketId,
    Guid AccountId,
    Guid? CounterAccountId,
    string? Description,
    string? Notes);

public sealed record PlannedCheck(int Month, Guid BucketId, AllocationStatus Status);

public sealed record PlannedBudget(decimal Stocks, decimal Crypto, decimal Travel, decimal OtherSavings);

public sealed record ReconciliationLine(int Month, string Measure, decimal? Workbook, decimal Imported, bool Matches);

public sealed record ImportPreview(
    ImportMapping Mapping,
    IReadOnlyList<PlannedTransaction> Transactions,
    IReadOnlyList<PlannedCheck> Checks,
    PlannedBudget? Budget,
    IReadOnlyList<ReconciliationLine> Reconciliation,
    IReadOnlyList<string> UnmappedCategories,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool CanCommit => Errors.Count == 0;
    public bool Reconciled => Reconciliation.All(r => r.Matches);
}

/// <summary>Pure transformation from raw workbook rows + mapping to ledger entries. Deterministic and idempotent.</summary>
public static class ImportPlanner
{
    /// <summary>Portuguese labels of the original template mapped to built-in category keys.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultCategoryKeys = new Dictionary<string, string>
    {
        ["habitacao"] = "housing",
        ["eletricidade"] = "electricity",
        ["agua / gas"] = "water-gas",
        ["internet"] = "internet",
        ["telemovel"] = "mobile",
        ["seguro de saude"] = "health-insurance",
        ["seguro automovel"] = "car-insurance",
        ["ginasio"] = "gym",
        ["streaming / subscricoes"] = "subscriptions",
        ["supermercado"] = "groceries",
        ["restaurantes"] = "restaurants",
        ["transportes"] = "transport",
        ["lazer / entretenimento"] = "entertainment",
        ["roupa"] = "clothing",
        ["saude / farmacia"] = "health",
        ["educacao"] = "education",
        ["prendas"] = "gifts",
        ["viagens"] = "travel",
        ["outro"] = "other",
        ["extra 1"] = "other",
        ["extra 2"] = "other",
        ["extra 3"] = "other",
    };

    private static readonly Dictionary<string, AllocationStatus> Statuses = new()
    {
        ["feito"] = AllocationStatus.Done,
        ["por fazer"] = AllocationStatus.Todo,
        ["parcial"] = AllocationStatus.Partial,
        ["n/a"] = AllocationStatus.NotApplicable,
    };

    public static IReadOnlyDictionary<string, Guid> SuggestCategories(RawWorkbook workbook) =>
        workbook.Rows.Where(r => r.Block is RawBlock.Fixed or RawBlock.Variable)
            .Select(r => FinanceTrackerReader.Normalize(r.CategoryLabel))
            .Concat(workbook.Config.Categories.Select(FinanceTrackerReader.Normalize))
            .Where(l => l.Length > 0)
            .Distinct()
            .ToDictionary(l => l, l => SystemCatalog.CategoryId(DefaultCategoryKeys.GetValueOrDefault(l, "other")));

    public static ImportPreview Plan(RawWorkbook workbook, ImportMapping mapping)
    {
        var errors = new List<string>();
        var warnings = new List<string>(workbook.Warnings);
        if (mapping.Year is < 1970 or > 2100)
        {
            errors.Add("Choose the year this workbook describes.");
        }

        if (mapping.MainAccountId is null)
        {
            errors.Add("Choose the account that receives income and pays expenses.");
        }

        var main = mapping.MainAccountId ?? Guid.Empty;
        var transactions = new List<PlannedTransaction>();
        var checks = new List<PlannedCheck>();
        var unmapped = new HashSet<string>();
        var year = Math.Clamp(mapping.Year, 1970, 2100);

        foreach (var row in workbook.Rows)
        {
            var period = new YearMonth(year, row.Month);
            var externalId = $"ft:{year}:{row.Month:D2}:{row.Cell}";
            switch (row.Block)
            {
                case RawBlock.Income:
                    transactions.Add(new PlannedTransaction(externalId, row.Month, TransactionType.Income,
                        period.LastDay, row.Amount, SystemCatalog.CategoryId(row.Label!), null, null, main, null,
                        row.Description, row.Notes));
                    break;

                case RawBlock.Allocation:
                    var bucketId = SystemCatalog.BucketId(row.Label!);
                    var isInvestment = row.Label is "stocks-etfs" or "crypto";
                    if (row.Amount > 0)
                    {
                        transactions.Add(new PlannedTransaction(externalId, row.Month,
                            isInvestment ? TransactionType.InvestmentContribution : TransactionType.Savings,
                            period.LastDay, row.Amount, null, null, bucketId, main,
                            isInvestment ? mapping.InvestmentAccountId : mapping.SavingsAccountId, row.Description,
                            null));
                    }

                    if (row.Status is not null)
                    {
                        if (Statuses.TryGetValue(FinanceTrackerReader.Normalize(row.Status), out var status))
                        {
                            checks.Add(new PlannedCheck(row.Month, bucketId, status));
                        }
                        else
                        {
                            warnings.Add($"{FinanceTrackerReader.MonthSheets[row.Month - 1]}!F{row.Cell[1..]}: unknown status ignored.");
                        }
                    }

                    break;

                case RawBlock.Fixed:
                case RawBlock.Variable:
                    var label = FinanceTrackerReader.Normalize(row.CategoryLabel);
                    if (!mapping.Categories.TryGetValue(label, out var categoryId))
                    {
                        unmapped.Add(row.CategoryLabel ?? "(blank)");
                        categoryId = SystemCatalog.CategoryId("other");
                    }

                    transactions.Add(new PlannedTransaction(externalId, row.Month, TransactionType.Expense,
                        ResolveDate(period, row, warnings), row.Amount, categoryId,
                        row.Block == RawBlock.Fixed ? ExpenseNature.Fixed : ExpenseNature.Variable, null, main, null,
                        row.Description, null));
                    break;
            }
        }

        if (mapping.MainAccountId is not null &&
            (mapping.InvestmentAccountId == mapping.MainAccountId || mapping.SavingsAccountId == mapping.MainAccountId))
        {
            errors.Add("Investment and savings accounts must differ from the main account (or be left empty).");
        }

        var c = workbook.Config;
        var budget = mapping.ImportBudget && c.StocksPercent is not null
            ? new PlannedBudget(c.StocksPercent ?? 0, c.CryptoPercent ?? 0, c.TravelPercent ?? 0, c.OtherSavingsPercent ?? 0)
            : null;
        if (budget is not null && budget.Stocks + budget.Crypto + budget.Travel + budget.OtherSavings > 1)
        {
            errors.Add("Configured percentages exceed 100%.");
        }

        return new ImportPreview(mapping, transactions, checks, budget, Reconcile(workbook, transactions),
            unmapped.Order().ToList(), errors, warnings);
    }

    /// <summary>
    /// Recomputes each month from the staged rows and compares with the workbook's own cached "Resumo Anual" values.
    /// A match to the cent proves nothing was lost or double counted.
    /// </summary>
    private static List<ReconciliationLine> Reconcile(RawWorkbook workbook, List<PlannedTransaction> planned)
    {
        var lines = new List<ReconciliationLine>();
        foreach (var expected in workbook.Expected)
        {
            var month = planned.Where(t => t.Month == expected.Month).ToList();
            void Add(string measure, decimal? workbookValue, decimal imported)
            {
                if (workbookValue is null && imported == 0)
                {
                    return;
                }

                lines.Add(new ReconciliationLine(expected.Month, measure, workbookValue, imported,
                    workbookValue is not null && Math.Abs(workbookValue.Value - imported) < 0.005m));
            }

            Add("income", expected.Income, month.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount));
            Add("fixed", expected.Fixed, month.Where(t => t.Nature == ExpenseNature.Fixed).Sum(t => t.Amount));
            Add("variable", expected.Variable, month.Where(t => t.Nature == ExpenseNature.Variable).Sum(t => t.Amount));
            Add("invested", expected.Invested,
                month.Where(t => t.Type == TransactionType.InvestmentContribution).Sum(t => t.Amount));
            Add("saved", expected.Saved, month.Where(t => t.Type == TransactionType.Savings).Sum(t => t.Amount));
        }

        return lines;
    }

    private static DateOnly ResolveDate(YearMonth period, RawRow row, List<string> warnings)
    {
        if (row.Date is { } date)
        {
            if (period.Contains(date))
            {
                return date;
            }

            warnings.Add($"{FinanceTrackerReader.MonthSheets[row.Month - 1]}!D{row.Cell[1..]}: date outside the month, using month end.");
        }

        if (row.Day is { } day)
        {
            return new DateOnly(period.Year, period.Month, Math.Min(day, DateTime.DaysInMonth(period.Year, period.Month)));
        }

        return period.LastDay;
    }
}
