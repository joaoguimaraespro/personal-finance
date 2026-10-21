using Finance.Application.Abstractions;
using Finance.Application.BrokerMovements;
using Finance.Application.Budgets;
using Finance.Domain.Accounts;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Reporting.Application.Calculations;
using SharedKernel;

namespace Reporting.Application.Queries;

/// <summary>
/// Single grouped query over the ledger; every report is computed in memory from its result. Buckets also count the
/// deposits broker syncs report into accounts linked to them (<see cref="BrokerTopUp"/>).
/// </summary>
public sealed class LedgerAggregates(IFinanceDb db, IBrokerDeposits? brokerDeposits = null)
{
    /// <summary>
    /// The first day the owner's money is tracked: the earliest opening balance of an everyday account (bank, savings,
    /// cash, card) or the earliest movement, whichever comes first. Null before anything is set up.
    /// </summary>
    public async Task<DateOnly?> TrackingStartAsync(CancellationToken ct)
    {
        AccountKind[] everyday = [AccountKind.Bank, AccountKind.Savings, AccountKind.Cash, AccountKind.CreditCard,
            AccountKind.Other];
        var opened = await db.Accounts.AsNoTracking().Where(a => everyday.Contains(a.Kind))
            .Select(a => (DateOnly?)a.OpeningBalanceOn).MinAsync(ct);
        var first = await db.Transactions.AsNoTracking().Select(t => (DateOnly?)t.OccurredOn).MinAsync(ct);
        return opened is null ? first : first is null || opened < first ? opened : first;
    }

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
                // The broker an investment entry went to: the same money as that broker's synced deposit.
                CounterAccountId = t.BucketId != null ? t.CounterAccountId : null,
                t.IsRefund,
            })
            .Select(g => new
            {
                Row = new LedgerRow(g.Key.Year, g.Key.Month, g.Key.Type, g.Key.Nature, g.Key.CategoryId,
                    g.Key.BucketId, g.Sum(t => t.BaseAmount), g.Count(), g.Key.CounterAccountId),
                g.Key.IsRefund,
            })
            .ToListAsync(ct);
        // A refund lowers its category's spending (and the month's expenses) instead of counting as income.
        var wholeRows = whole.Select(w => w.IsRefund ? w.Row with { Amount = -w.Row.Amount } : w.Row).ToList();

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

        var rows = wholeRows.Concat(lines).Concat(splitCounts).ToList();

        // Capital repaid on loans: transfers into loan accounts. Not an expense (the debt goes down), but money that
        // left the everyday accounts, so the month's free cash flow accounts for it.
        var loanIds = await db.Accounts.AsNoTracking().Where(a => a.Kind == AccountKind.Loan).Select(a => a.Id)
            .ToListAsync(ct);
        var repaid = loanIds.Count == 0
            ? []
            : await db.Transactions.AsNoTracking()
                .Where(t => t.OccurredOn >= start && t.OccurredOn <= end && t.Type == TransactionType.Transfer &&
                            t.CounterAccountId != null && loanIds.Contains(t.CounterAccountId.Value) &&
                            !loanIds.Contains(t.AccountId))
                .GroupBy(t => new { t.OccurredOn.Year, t.OccurredOn.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Amount = g.Sum(t => t.BaseAmount) })
                .ToListAsync(ct);
        var linked = await db.Accounts.AsNoTracking()
            .Where(a => a.AllocationBucketId != null)
            .Select(a => new { a.Id, BucketId = a.AllocationBucketId!.Value })
            .ToListAsync(ct);
        var deposits = linked.Count > 0 && brokerDeposits is not null
            ? await brokerDeposits.MonthlyAsync(from, to, ct)
            : [];
        // Deposits and withdrawals the owner reviews: pending ones are shown apart, recorded ones are already in the
        // ledger as their transaction (which must not shrink the synced deposits still counted as they are).
        var reviewed = linked.Count > 0 && brokerDeposits is not null
            ? await BrokerMovementEndpoints.ListAsync(db, brokerDeposits, start, end, ct)
            : [];
        var linkedIds = reviewed.Where(m => m.TransactionId is not null).Select(m => m.TransactionId!.Value).ToList();
        var linkedEntries = linkedIds.Count == 0
            ? []
            : await db.Transactions.AsNoTracking().Where(t => linkedIds.Contains(t.Id))
                .Select(t => new { t.OccurredOn, t.Type, t.BucketId, t.CounterAccountId, t.BaseAmount })
                .ToListAsync(ct);

        var result = new List<MonthTotals>();
        for (var p = from; p <= to; p = p.Next())
        {
            var month = rows.Where(r => r.Year == p.Year && r.Month == p.Month).ToList();
            var expenses = month.Where(r => r.Type == TransactionType.Expense).ToList();
            var byBucket = month.Where(r => r.BucketId != null).GroupBy(r => r.BucketId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(Signed));
            var fromBrokers = new Dictionary<Guid, decimal>();
            var pendingFromBrokers = new Dictionary<Guid, decimal>();
            foreach (var account in linked)
            {
                var deposited = deposits.Where(d => d.AccountId == account.Id && d.Period == p).Sum(d => d.NetDeposits);
                var recorded = month.Where(r => r.BucketId == account.BucketId && r.CounterAccountId == account.Id)
                    .Sum(Signed) - linkedEntries
                    .Where(t => YearMonth.From(t.OccurredOn) == p && t.BucketId == account.BucketId &&
                                t.CounterAccountId == account.Id)
                    .Sum(t => t.Type == TransactionType.InvestmentSale ? -t.BaseAmount : t.BaseAmount);
                var pending = reviewed.Where(m => m.AccountId == account.Id && m.State == BrokerMovementState.Pending &&
                                                  YearMonth.From(m.On) == p).Sum(m => m.BaseAmount);
                if (pending != 0)
                {
                    pendingFromBrokers[account.BucketId] = pendingFromBrokers.GetValueOrDefault(account.BucketId) + pending;
                }

                if (BrokerTopUp(deposited, recorded) is var topUp and > 0)
                {
                    fromBrokers[account.BucketId] = fromBrokers.GetValueOrDefault(account.BucketId) + topUp;
                    byBucket[account.BucketId] = byBucket.GetValueOrDefault(account.BucketId) + topUp;
                }
            }

            result.Add(new MonthTotals(
                p,
                month.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount),
                expenses.Where(r => r.Nature == ExpenseNature.Fixed).Sum(r => r.Amount),
                expenses.Where(r => r.Nature != ExpenseNature.Fixed).Sum(r => r.Amount),
                // Sales give money back to the bucket: "invested" is net of them and never counted as income.
                byBucket,
                expenses.Where(r => r.CategoryId != null).GroupBy(r => r.CategoryId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)),
                month.Where(r => r.Type == TransactionType.Income && r.CategoryId != null)
                    .GroupBy(r => r.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)),
                month.Sum(r => r.Count),
                month.Where(r => r.Type == TransactionType.InvestmentContribution).Sum(r => r.Amount),
                month.Where(r => r.Type == TransactionType.InvestmentSale).Sum(r => r.Amount),
                fromBrokers,
                repaid.Where(x => x.Year == p.Year && x.Month == p.Month).Sum(x => x.Amount),
                pendingFromBrokers));
        }

        return result;
    }

    private static decimal Signed(LedgerRow r) => r.Type == TransactionType.InvestmentSale ? -r.Amount : r.Amount;

    /// <summary>
    /// What a broker's synced deposits add to its bucket in a month: the part not already recorded in the ledger as
    /// going to that broker. Recording the same money in both places counts it once (the larger of the two).
    /// </summary>
    public static decimal BrokerTopUp(decimal deposited, decimal recordedToBroker) =>
        Math.Max(deposited - Math.Max(recordedToBroker, 0), 0);

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
    Guid? BucketId, decimal Amount, int Count, Guid? CounterAccountId = null);
