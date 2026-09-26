using SharedKernel;

namespace Finance.Domain.Budgets;

public enum BudgetTarget
{
    /// <summary>An allocation bucket (investment or savings), e.g. "Stocks / ETFs 25%".</summary>
    Bucket = 0,

    /// <summary>The overall expense pool, e.g. "Expense budget 65%".</summary>
    ExpensePool = 1,

    /// <summary>A single expense category, e.g. "Restaurants €150".</summary>
    Category = 2,
}

public enum BudgetMode
{
    PercentOfIncome = 0,
    FixedAmount = 1,

    /// <summary>Whatever income is left after the other pool-level items (the spreadsheet's "1 − B7 − B11").</summary>
    Remainder = 2,
}

/// <summary>
/// A budget version effective from a given month until the next version. Unlike the spreadsheet, changing
/// percentages never rewrites the targets of past months.
/// </summary>
public sealed class Budget : Entity, IAuditable
{
    private readonly List<BudgetItem> _items = [];

    private Budget() { }

    public DateOnly EffectiveFrom { get; private set; }
    public string? Note { get; private set; }
    public IReadOnlyList<BudgetItem> Items => _items;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static Result<Budget> Create(YearMonth effectiveFrom, IEnumerable<BudgetItemSpec> items, string? note = null)
    {
        var budget = new Budget { EffectiveFrom = effectiveFrom.FirstDay, Note = note };
        var result = budget.ReplaceItems(items);
        return result.IsSuccess ? budget : Result.Failure<Budget>(result.Error);
    }

    public Result ReplaceItems(IEnumerable<BudgetItemSpec> specs)
    {
        var list = specs.ToList();
        var error = Validate(list);
        if (error is not null)
        {
            return error;
        }

        _items.Clear();
        _items.AddRange(list.Select(s => new BudgetItem(Id, s)));
        return Result.Success();
    }

    public void SetNote(string? note) => Note = note;

    private static Error? Validate(List<BudgetItemSpec> items)
    {
        if (items.Any(i => i.Value < 0))
        {
            return Error.Validation("Budget.Value", "Budget values cannot be negative.");
        }

        if (items.Any(i => i.Mode == BudgetMode.PercentOfIncome && i.Value > 1))
        {
            return Error.Validation("Budget.Percent", "Percentages are fractions between 0 and 1.");
        }

        if (items.Count(i => i.Mode == BudgetMode.Remainder) > 1)
        {
            return Error.Validation("Budget.Remainder", "Only one item can take the remainder.");
        }

        var poolPercent = items.Where(i => i.Target != BudgetTarget.Category && i.Mode == BudgetMode.PercentOfIncome)
            .Sum(i => i.Value);
        if (poolPercent > 1)
        {
            return Error.Validation("Budget.Total", "Allocations exceed 100% of income.");
        }

        var duplicates = items.GroupBy(i => (i.Target, i.BucketId, i.CategoryId)).Any(g => g.Count() > 1);
        return duplicates ? Error.Validation("Budget.Duplicate", "Each target can only appear once.") : null;
    }
}

public sealed record BudgetItemSpec(BudgetTarget Target, BudgetMode Mode, decimal Value, Guid? BucketId = null,
    Guid? CategoryId = null);

public sealed class BudgetItem
{
    private BudgetItem() { }

    internal BudgetItem(Guid budgetId, BudgetItemSpec spec)
    {
        BudgetId = budgetId;
        Target = spec.Target;
        Mode = spec.Mode;
        Value = spec.Value;
        BucketId = spec.Target == BudgetTarget.Bucket ? spec.BucketId : null;
        CategoryId = spec.Target == BudgetTarget.Category ? spec.CategoryId : null;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public Guid BudgetId { get; private set; }
    public BudgetTarget Target { get; private set; }
    public BudgetMode Mode { get; private set; }

    /// <summary>A fraction (0.25 = 25%) for percentages, EUR otherwise. Ignored for remainder.</summary>
    public decimal Value { get; private set; }
    public Guid? BucketId { get; private set; }
    public Guid? CategoryId { get; private set; }
}
