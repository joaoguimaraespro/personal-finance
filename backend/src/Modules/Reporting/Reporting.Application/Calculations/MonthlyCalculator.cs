using Finance.Domain.Budgets;
using SharedKernel;

namespace Reporting.Application.Calculations;

public enum BalanceStatus
{
    Positive = 0,
    Negative = 1,
}

/// <param name="FromBrokers">Part of <paramref name="Actual"/> that comes from deposits synced at linked brokers.</param>
public sealed record BucketLine(Guid BucketId, string Name, bool IsInvestment, decimal? Target, decimal Actual,
    decimal? Difference, decimal FromBrokers = 0);

public sealed record MonthlySummary(
    YearMonth Period,
    decimal Income,
    decimal FixedExpenses,
    decimal VariableExpenses,
    decimal TotalExpenses,
    decimal Invested,
    decimal Saved,
    decimal NetBalance,
    decimal FreeCashFlow,
    decimal? SavingsRate,
    decimal? InvestmentRate,
    decimal? SavingsOnlyRate,
    decimal? ExpenseBudget,
    decimal? ExpenseBudgetBalance,
    decimal? InvestmentTarget,
    decimal? SavingsTarget,
    decimal? Unallocated,
    BalanceStatus Status,
    IReadOnlyList<BucketLine> Buckets,
    int TransactionCount,
    decimal InvestmentPurchases = 0,
    decimal InvestmentSales = 0);

/// <summary>
/// Deterministic port of the spreadsheet's month sheet ("RESUMO MENSAL" block). Formula references are to the
/// original cells so the behaviour can be audited line by line.
/// </summary>
public static class MonthlyCalculator
{
    public static MonthlySummary Compute(MonthTotals t, IReadOnlyList<BucketInfo> buckets, Budget? budget)
    {
        var invested = buckets.Where(b => b.IsInvestment).Sum(b => t.ByBucket.GetValueOrDefault(b.Id)); // D11+D12
        var saved = buckets.Where(b => !b.IsInvestment).Sum(b => t.ByBucket.GetValueOrDefault(b.Id));   // D13+D14
        var totalExpenses = t.FixedExpenses + t.VariableExpenses;                                        // C31+C47
        var netBalance = t.Income - totalExpenses;                                                        // I12

        var targets = BudgetTargets.Resolve(budget, t.Income);
        var lines = buckets
            .Where(b => t.ByBucket.ContainsKey(b.Id) || targets.Buckets.ContainsKey(b.Id))
            .Select(b =>
            {
                var actual = t.ByBucket.GetValueOrDefault(b.Id);
                decimal? target = targets.Buckets.TryGetValue(b.Id, out var v) ? v : null;
                return new BucketLine(b.Id, b.Name, b.IsInvestment, target, actual, actual - target, // E = D − C
                    t.FromBrokers?.GetValueOrDefault(b.Id) ?? 0);
            })
            .ToList();

        return new MonthlySummary(
            t.Period,
            t.Income,
            t.FixedExpenses,
            t.VariableExpenses,
            totalExpenses,
            invested,
            saved,
            netBalance,
            FreeCashFlow: netBalance - invested - saved,
            SavingsRate: Ratio(invested + saved, t.Income),      // I13 / Resumo J
            InvestmentRate: Ratio(invested, t.Income),           // Resumo N
            SavingsOnlyRate: Ratio(saved, t.Income),             // Resumo O
            ExpenseBudget: targets.ExpensePool,                  // C15
            ExpenseBudgetBalance: targets.ExpensePool - totalExpenses, // I9
            InvestmentTarget: SumTargets(lines, investment: true),     // C11 + C12
            SavingsTarget: SumTargets(lines, investment: false),       // C13 + C14
            Unallocated: budget is null ? null : t.Income - targets.PoolTotal, // C16
            Status: netBalance >= 0 ? BalanceStatus.Positive : BalanceStatus.Negative,
            lines,
            t.TransactionCount,
            InvestmentPurchases: t.InvestmentPurchases,
            InvestmentSales: t.InvestmentSales);
    }

    private static decimal? SumTargets(List<BucketLine> lines, bool investment)
    {
        var withTarget = lines.Where(l => l.IsInvestment == investment && l.Target is not null).ToList();
        return withTarget.Count == 0 ? null : withTarget.Sum(l => l.Target!.Value);
    }

    /// <summary>Division that yields "no value" instead of 0 or an error when there is no income (the NA() cells).</summary>
    public static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0 ? null : decimal.Round(numerator / denominator, 6);
}

/// <summary>Budget item targets converted to EUR for a month's income.</summary>
public sealed record ResolvedTargets(
    IReadOnlyDictionary<Guid, decimal> Buckets,
    IReadOnlyDictionary<Guid, decimal> Categories,
    decimal? ExpensePool,
    decimal PoolTotal);

public static class BudgetTargets
{
    public static ResolvedTargets Resolve(Budget? budget, decimal income)
    {
        if (budget is null)
        {
            return new ResolvedTargets(new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), null, 0);
        }

        decimal Amount(BudgetItem i) => i.Mode == BudgetMode.PercentOfIncome ? income * i.Value : i.Value;

        var buckets = budget.Items.Where(i => i.Target == BudgetTarget.Bucket && i.Mode != BudgetMode.Remainder)
            .ToDictionary(i => i.BucketId!.Value, Amount);
        var categories = budget.Items.Where(i => i.Target == BudgetTarget.Category)
            .ToDictionary(i => i.CategoryId!.Value, Amount);

        var pool = budget.Items.FirstOrDefault(i => i.Target == BudgetTarget.ExpensePool);
        var fixedPoolItems = buckets.Values.Sum() + (pool is { Mode: not BudgetMode.Remainder } ? Amount(pool) : 0);

        // "Orçamento de Despesas = 1 − investimentos − poupanças": the remainder item takes what is left.
        var remainderItem = budget.Items.FirstOrDefault(i => i.Mode == BudgetMode.Remainder);
        var remainder = income - fixedPoolItems;
        if (remainderItem is { Target: BudgetTarget.Bucket, BucketId: { } rb })
        {
            buckets[rb] = remainder;
        }

        decimal? expensePool = pool switch
        {
            null => null,
            { Mode: BudgetMode.Remainder } => remainder,
            _ => Amount(pool),
        };

        var poolTotal = buckets.Values.Sum() + (expensePool ?? 0);
        return new ResolvedTargets(buckets, categories, expensePool, poolTotal);
    }
}
