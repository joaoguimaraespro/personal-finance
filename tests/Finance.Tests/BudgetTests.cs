using Finance.Domain.Budgets;
using SharedKernel;

namespace Finance.Tests;

public sealed class BudgetTests
{
    private static readonly YearMonth Jan = new(2026, 1);

    [Fact]
    public void Allocations_over_100_percent_are_rejected() =>
        Budget.Create(Jan,
        [
            new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0.7m, Guid.NewGuid()),
            new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.PercentOfIncome, 0.4m),
        ]).Error.Code.ShouldBe("Budget.Total");

    [Fact]
    public void Only_one_remainder_is_allowed() =>
        Budget.Create(Jan,
        [
            new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.Remainder, 0, Guid.NewGuid()),
            new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.Remainder, 0),
        ]).Error.Code.ShouldBe("Budget.Remainder");

    [Fact]
    public void Percentages_are_fractions() =>
        Budget.Create(Jan, [new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.PercentOfIncome, 65m)])
            .Error.Code.ShouldBe("Budget.Percent");

    [Fact]
    public void Category_budgets_do_not_count_towards_the_income_split()
    {
        var result = Budget.Create(Jan,
        [
            new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.PercentOfIncome, 1m),
            new BudgetItemSpec(BudgetTarget.Category, BudgetMode.PercentOfIncome, 0.1m, CategoryId: Guid.NewGuid()),
        ]);

        result.IsSuccess.ShouldBeTrue();
    }
}
