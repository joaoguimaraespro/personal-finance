using Finance.Domain;
using Finance.Domain.Budgets;
using Reporting.Application.Calculations;
using SharedKernel;

namespace Reporting.Tests;

/// <summary>
/// Each test reproduces a formula of the original workbook with concrete numbers, so a regression shows up as a
/// difference against what the spreadsheet would have displayed.
/// </summary>
public sealed class ExcelParityTests
{
    private static readonly Guid Stocks = SystemCatalog.BucketId("stocks-etfs");
    private static readonly Guid Crypto = SystemCatalog.BucketId("crypto");
    private static readonly Guid Travel = SystemCatalog.BucketId("travel-fund");
    private static readonly Guid OtherSavings = SystemCatalog.BucketId("other-savings");

    private static readonly IReadOnlyList<BucketInfo> Buckets =
    [
        new(Stocks, "Stocks / ETFs", true),
        new(Crypto, "Crypto", true),
        new(Travel, "Travel fund", false),
        new(OtherSavings, "Other savings", false),
    ];

    /// <summary>The template's default configuration: 25% / 0% / 5% / 5%, expenses = 1 − 0.25 − 0.10 = 65%.</summary>
    private static Budget TemplateBudget() => Budget.Create(new YearMonth(2026, 1),
    [
        new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0.25m, Stocks),
        new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0m, Crypto),
        new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0.05m, Travel),
        new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0.05m, OtherSavings),
        new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.Remainder, 0),
    ]).Value;

    private static MonthTotals Month(int m, decimal income, decimal fixedExp, decimal variable, decimal stocks = 0,
        decimal crypto = 0, decimal travel = 0, decimal other = 0) => new(
        new YearMonth(2026, m), income, fixedExp, variable,
        new Dictionary<Guid, decimal> { [Stocks] = stocks, [Crypto] = crypto, [Travel] = travel, [OtherSavings] = other },
        new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), 1);

    [Fact]
    public void Month_sheet_summary_block_matches_workbook_formulas()
    {
        // Jan: B5 salary 2 000 + B6 other 500 → B7 = 2 500
        var s = MonthlyCalculator.Compute(Month(1, 2500m, 900m, 450.50m, stocks: 600m, travel: 125m, other: 100m),
            Buckets, TemplateBudget());

        s.Income.ShouldBe(2500m);                   // I4 = B7
        s.ExpenseBudget.ShouldBe(1625m);            // I5 = C15 = B7 × 65%
        s.FixedExpenses.ShouldBe(900m);             // I6 = C31
        s.VariableExpenses.ShouldBe(450.50m);       // I7 = C47
        s.TotalExpenses.ShouldBe(1350.50m);         // I8 = C31 + C47
        s.ExpenseBudgetBalance.ShouldBe(274.50m);   // I9 = C15 − (C31 + C47)
        s.Invested.ShouldBe(600m);                  // I10 = D11 + D12
        s.Saved.ShouldBe(225m);                     // I11 = D13 + D14
        s.NetBalance.ShouldBe(1149.50m);            // I12 = B7 − (C31 + C47): investments are NOT subtracted
        s.SavingsRate.ShouldBe(0.33m);              // I13 = (D11+D12+D13+D14) / B7
        s.Unallocated.ShouldBe(0m);                 // C16 = B7 − SUM(C11:C15)
        s.InvestmentTarget.ShouldBe(625m);          // C11 + C12
        s.SavingsTarget.ShouldBe(250m);             // C13 + C14
        s.Status.ShouldBe(BalanceStatus.Positive);  // Dashboard I = IF(saldo >= 0, "Positivo", "Negativo")
        s.FreeCashFlow.ShouldBe(324.50m);           // new: what is really left after setting money aside
    }

    [Fact]
    public void Allocation_rows_compute_budget_and_difference()
    {
        var s = MonthlyCalculator.Compute(Month(1, 2500m, 0, 0, stocks: 600m, travel: 125m), Buckets, TemplateBudget());

        var stocks = s.Buckets.Single(b => b.BucketId == Stocks);
        stocks.Target.ShouldBe(625m);        // C11 = B7 × B11
        stocks.Difference.ShouldBe(-25m);    // E11 = D11 − C11
        s.Buckets.Single(b => b.BucketId == Travel).Difference.ShouldBe(0m);
    }

    [Fact]
    public void Rates_are_empty_not_zero_when_there_is_no_income()
    {
        // Resumo Anual J/N/O: IF(B7=0, "" / NA(), ...)
        var s = MonthlyCalculator.Compute(Month(2, 0, 100m, 0), Buckets, TemplateBudget());

        s.SavingsRate.ShouldBeNull();
        s.InvestmentRate.ShouldBeNull();
        s.SavingsOnlyRate.ShouldBeNull();
        s.NetBalance.ShouldBe(-100m);
        s.Status.ShouldBe(BalanceStatus.Negative);
    }

    [Fact]
    public void Annual_summary_matches_resumo_anual_including_unweighted_average()
    {
        var months = Enumerable.Range(1, 12).Select(m => m switch
        {
            1 => Month(1, 2000m, 800m, 400m, stocks: 500m, other: 100m),   // rate 30%
            2 => Month(2, 4000m, 800m, 600m, stocks: 1000m, other: 200m),  // rate 30%
            3 => Month(3, 2000m, 800m, 500m, stocks: 100m),                // rate 5%
            _ => Month(m, 0, 0, 0),
        }).Select(t => MonthlyCalculator.Compute(t, Buckets, TemplateBudget())).ToList();

        var annual = AnnualCalculator.Compute(2026, months);

        annual.Totals.Income.ShouldBe(8000m);                   // B16
        annual.Totals.ExpenseBudget.ShouldBe(5200m);            // C16 = Σ 65% × income
        annual.Totals.TotalExpenses.ShouldBe(3900m);            // F16
        annual.Totals.Invested.ShouldBe(1600m);                 // G16
        annual.Totals.Saved.ShouldBe(300m);                     // H16
        annual.Totals.NetBalance.ShouldBe(4100m);               // I16
        annual.Totals.AverageMonthlySavingsRate.ShouldBe(0.216667m); // J16 = AVERAGEIF(J4:J15,"<>") = (0.3+0.3+0.05)/3
        annual.Totals.WeightedSavingsRate.ShouldBe(0.2375m);    // (1600 + 300) / 8000

        annual.Months[2].CumulativeInvested.ShouldBe(1600m);    // L6 running sum
        annual.Months[2].CumulativeSaved.ShouldBe(300m);        // M6
        annual.Months[1].Month.InvestmentRate.ShouldBe(0.25m);  // N5 = (D11+D12)/B7
        annual.Months[1].Month.SavingsOnlyRate.ShouldBe(0.05m); // O5 = (D13+D14)/B7
        annual.Months[5].HasIncome.ShouldBeFalse();
    }

    [Fact]
    public void Budget_changes_do_not_rewrite_history()
    {
        var older = TemplateBudget();
        var newer = Budget.Create(new YearMonth(2026, 7),
        [
            new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.PercentOfIncome, 0.40m, Stocks),
            new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.Remainder, 0),
        ]).Value;

        var march = MonthlyCalculator.Compute(Month(3, 1000m, 0, 0), Buckets, older);
        var august = MonthlyCalculator.Compute(Month(8, 1000m, 0, 0), Buckets, newer);

        march.ExpenseBudget.ShouldBe(650m);
        august.ExpenseBudget.ShouldBe(600m);
    }

    [Fact]
    public void Fixed_amount_and_category_budgets_are_supported()
    {
        var restaurants = SystemCatalog.CategoryId("restaurants");
        var budget = Budget.Create(new YearMonth(2026, 1),
        [
            new BudgetItemSpec(BudgetTarget.Bucket, BudgetMode.FixedAmount, 500m, Stocks),
            new BudgetItemSpec(BudgetTarget.ExpensePool, BudgetMode.FixedAmount, 1500m),
            new BudgetItemSpec(BudgetTarget.Category, BudgetMode.FixedAmount, 150m, CategoryId: restaurants),
        ]).Value;

        var targets = BudgetTargets.Resolve(budget, 2500m);

        targets.Buckets[Stocks].ShouldBe(500m);
        targets.ExpensePool.ShouldBe(1500m);
        targets.Categories[restaurants].ShouldBe(150m);
        targets.PoolTotal.ShouldBe(2000m);
    }
}
