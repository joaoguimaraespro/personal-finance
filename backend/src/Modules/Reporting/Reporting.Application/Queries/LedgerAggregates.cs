using Finance.Application.Abstractions;
using Finance.Application.Budgets;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Reporting.Application.Calculations;
using SharedKernel;

namespace Reporting.Application.Queries;

/// <summary>Single grouped query over the ledger; every report is computed in memory from its result.</summary>
public sealed class LedgerAggregates(IFinanceDb db)
{
    public async Task<IReadOnlyList<MonthTotals>> MonthTotalsAsync(YearMonth from, YearMonth to, CancellationToken ct)
    {
        var start = from.FirstDay;
        var end = to.LastDay;
        var inRange = db.Transactions.AsNoTracking()
            .Where(t => t.OccurredOn >= start && t.OccurredOn <= end && t.Type != TransactionType.Transfer);

        // Transactions with a single category are grouped as they are.
        var whole = await inRange.Where(t => !t.Splits.Any())
            .GroupBy(t => new
            {
                t.OccurredOn.Year,
                t.OccurredOn.Month,
                t.Type,
                t.Nature,
                t.CategoryId,
                t.BucketId,
            })
            .Select(g => new LedgerRow(g.Key.Year, g.Key.Month, g.Key.Type, g.Key.Nature, g.Key.CategoryId,
                g.Key.BucketId, g.Sum(t => t.BaseAmount), g.Count()))
            .ToListAsync(ct);

        // A split transaction counts by its lines: each line's category, nature and EUR amount (the lines add up
        // exactly to the transaction's EUR amount, so totals are unchanged).
        var lines = await inRange
            .SelectMany(t => t.Splits, (t, s) => new
            {
                t.OccurredOn.Year,
                t.OccurredOn.Month,
                t.Type,
                s.Nature,
                s.CategoryId,
                s.BaseAmount,
            })
            .GroupBy(x => new { x.Year, x.Month, x.Type, x.Nature, x.CategoryId })
            .Select(g => new LedgerRow(g.Key.Year, g.Key.Month, g.Key.Type, g.Key.Nature, g.Key.CategoryId, null,
                g.Sum(x => x.BaseAmount), 0))
            .ToListAsync(ct);

        // Still one transaction each, however many lines.
        var splitCounts = await inRange.Where(t => t.Splits.Any())
            .GroupBy(t => new { t.OccurredOn.Year, t.OccurredOn.Month, t.Type })
            .Select(g => new LedgerRow(g.Key.Year, g.Key.Month, g.Key.Type, null, null, null, 0, g.Count()))
            .ToListAsync(ct);

        var rows = whole.Concat(lines).Concat(splitCounts).ToList();

        var result = new List<MonthTotals>();
        for (var p = from; p <= to; p = p.Next())
        {
            var month = rows.Where(r => r.Year == p.Year && r.Month == p.Month).ToList();
            var expenses = month.Where(r => r.Type == TransactionType.Expense).ToList();
            result.Add(new MonthTotals(
                p,
                month.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount),
                expenses.Where(r => r.Nature == ExpenseNature.Fixed).Sum(r => r.Amount),
                expenses.Where(r => r.Nature != ExpenseNature.Fixed).Sum(r => r.Amount),
                // Sales give money back to the bucket: "invested" is net of them and never counted as income.
                month.Where(r => r.BucketId != null).GroupBy(r => r.BucketId!.Value)
                    .ToDictionary(g => g.Key,
                        g => g.Sum(r => r.Type == TransactionType.InvestmentSale ? -r.Amount : r.Amount)),
                expenses.Where(r => r.CategoryId != null).GroupBy(r => r.CategoryId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)),
                month.Where(r => r.Type == TransactionType.Income && r.CategoryId != null)
                    .GroupBy(r => r.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)),
                month.Sum(r => r.Count),
                month.Where(r => r.Type == TransactionType.InvestmentContribution).Sum(r => r.Amount),
                month.Where(r => r.Type == TransactionType.InvestmentSale).Sum(r => r.Amount)));
        }

        return result;
    }

    public async Task<IReadOnlyList<BucketInfo>> BucketsAsync(CancellationToken ct) =>
        await db.Buckets.AsNoTracking().OrderBy(b => b.Group).ThenBy(b => b.SortOrder)
            .Select(b => new BucketInfo(b.Id, b.Name, b.Group == BucketGroup.Investment))
            .ToListAsync(ct);

    /// <summary>Budget versions effective in each month of the range.</summary>
    public async Task<Func<YearMonth, Budget?>> BudgetResolverAsync(YearMonth to, CancellationToken ct)
    {
        var end = to.LastDay;
        var versions = await db.Budgets.AsNoTracking().Include(b => b.Items)
            .Where(b => b.EffectiveFrom <= end)
            .OrderByDescending(b => b.EffectiveFrom)
            .ToListAsync(ct);
        return period => versions.FirstOrDefault(v => v.EffectiveFrom <= period.FirstDay);
    }

    public async Task<IReadOnlyList<MonthlySummary>> MonthlySummariesAsync(YearMonth from, YearMonth to,
        CancellationToken ct)
    {
        var totals = await MonthTotalsAsync(from, to, ct);
        var buckets = await BucketsAsync(ct);
        var budgetFor = await BudgetResolverAsync(to, ct);
        return totals.Select(t => MonthlyCalculator.Compute(t, buckets, budgetFor(t.Period))).ToList();
    }

    public Task<Budget?> EffectiveBudgetAsync(YearMonth period, CancellationToken ct) =>
        BudgetEndpoints.FindEffectiveAsync(db, period, ct);
}

/// <summary>One grouped slice of the ledger: a group of whole transactions, of split lines, or only a count.</summary>
internal sealed record LedgerRow(int Year, int Month, TransactionType Type, ExpenseNature? Nature, Guid? CategoryId,
    Guid? BucketId, decimal Amount, int Count);
