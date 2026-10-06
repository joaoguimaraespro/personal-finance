using Finance.Application.Abstractions;
using Finance.Application.Goals;
using Finance.Domain.Transactions;
using Investments.Application.Abstractions;
using Investments.Application.Portfolio;
using Microsoft.EntityFrameworkCore;
using Reporting.Application.Calculations;
using Reporting.Application.Queries;
using SharedKernel;

namespace Exports.Application;

/// <summary>
/// One export row. A split transaction becomes one row per category line: <see cref="Split"/> is "1/2", "2/2", …
/// and Amount/AmountEur are the line's, so summing a column by category gives the report figures.
/// </summary>
public sealed record TransactionRow(DateOnly Date, string Type, string? Category, string? Nature, string Account,
    string? CounterAccount, string? Bucket, string? Description, decimal Amount, string Currency, decimal FxRate,
    decimal AmountEur, string Source, string? Notes, string? Split = null, string? SplitNote = null);

/// <summary>Reads everything an export needs. All figures come from the same calculators as the app.</summary>
public sealed class ExportData(IFinanceDb finance, IInvestmentsDb investments, LedgerAggregates ledger,
    PortfolioQueries portfolio, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<TransactionRow>> TransactionsAsync(DateOnly from, DateOnly to,
        TransactionType[]? types, CancellationToken ct)
    {
        var query = finance.Transactions.AsNoTracking().Where(t => t.OccurredOn >= from && t.OccurredOn <= to);
        if (types is { Length: > 0 })
        {
            query = query.Where(t => types.Contains(t.Type));
        }

        var rows = await (
                from t in query
                join a in finance.Accounts on t.AccountId equals a.Id
                join ca in finance.Accounts on t.CounterAccountId equals ca.Id into cas
                from ca in cas.DefaultIfEmpty()
                join c in finance.Categories on t.CategoryId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                join b in finance.Buckets on t.BucketId equals b.Id into bs
                from b in bs.DefaultIfEmpty()
                orderby t.OccurredOn, t.CreatedAtUtc
                select new
                {
                    Row = new TransactionRow(t.OccurredOn, t.Type.ToString(), c == null ? null : c.Name,
                        t.Nature == null ? null : t.Nature.ToString(), a.Name, ca == null ? null : ca.Name,
                        b == null ? null : b.Name, t.Description, t.OriginalAmount, t.OriginalCurrency, t.FxRate,
                        t.BaseAmount, t.Source.ToString(), t.Notes, null, null),
                    Lines = (from s in t.Splits
                            join sc in finance.Categories on s.CategoryId equals sc.Id
                            orderby s.Position
                            select new { sc.Name, s.Nature, s.OriginalAmount, s.BaseAmount, s.Note })
                        .ToList(),
                })
            .ToListAsync(ct);

        return rows.SelectMany(x => x.Lines.Count == 0
                ? [x.Row]
                : x.Lines.Select((l, i) => x.Row with
                {
                    Category = l.Name,
                    Nature = l.Nature?.ToString(),
                    Amount = l.OriginalAmount,
                    AmountEur = l.BaseAmount,
                    Split = $"{i + 1}/{x.Lines.Count}",
                    SplitNote = l.Note,
                }))
            .ToList();
    }

    public Task<IReadOnlyList<MonthlySummary>> MonthsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        ledger.MonthlySummariesAsync(YearMonth.From(from), YearMonth.From(to), ct);

    public Task<IReadOnlyList<GoalDto>> GoalsAsync(CancellationToken ct) => GoalEndpoints.ListAsync(finance, Today, true, ct);

    public async Task<IReadOnlyList<(string EffectiveFrom, string Target, string Mode, decimal Value, string? Name, string? Note)>>
        BudgetsAsync(CancellationToken ct)
    {
        var budgets = await finance.Budgets.AsNoTracking().Include(b => b.Items).OrderBy(b => b.EffectiveFrom).ToListAsync(ct);
        var buckets = await finance.Buckets.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var categories = await finance.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return budgets.SelectMany(b => b.Items.Select(i => (
            YearMonth.From(b.EffectiveFrom).ToString(), i.Target.ToString(), i.Mode.ToString(), i.Value,
            i.BucketId is { } bid ? buckets.GetValueOrDefault(bid) : i.CategoryId is { } cid ? categories.GetValueOrDefault(cid) : null,
            b.Note))).ToList();
    }

    public Task<PortfolioSummary> PortfolioAsync(CancellationToken ct) => portfolio.SummaryAsync(new PortfolioScope(), ct);

    public Task<IReadOnlyList<PositionLine>> PositionsAsync(CancellationToken ct) => portfolio.PositionsAsync(new PortfolioScope(), ct);

    public Task<IReadOnlyList<AllocationLine>> AllocationAsync(CancellationToken ct) => portfolio.AllocationAsync(new PortfolioScope(), ct);

    public Task<DividendSummary> DividendsAsync(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        portfolio.DividendsAsync(new PortfolioScope(), from, to, ct);

    public Task<PerformanceReport> PerformanceAsync(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        portfolio.PerformanceAsync(new PortfolioScope(), from, to, ct);

    public async Task<IReadOnlyList<(DateTimeOffset At, string Account, string Symbol, string Side, decimal Quantity, decimal Price,
        string Currency, decimal Costs, decimal AmountEur, decimal? RealizedEur, string Source)>> TradesAsync(CancellationToken ct)
    {
        var accounts = await finance.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var rows = await (from t in investments.Trades.AsNoTracking()
                join s in investments.Securities.AsNoTracking() on t.SecurityId equals s.Id
                orderby t.ExecutedAtUtc
                select new { t, s.Symbol })
            .ToListAsync(ct);
        return rows.Select(r => (r.t.ExecutedAtUtc, accounts.GetValueOrDefault(r.t.AccountId, "?"), r.Symbol, r.t.Side.ToString(),
            r.t.Quantity, r.t.Price, r.t.Currency, r.t.CostsBase, r.t.BaseAmount, r.t.RealizedPnlBase, r.t.Source.ToString())).ToList();
    }

    public async Task<IReadOnlyList<(DateOnly Date, decimal Assets, decimal Liabilities, decimal NetWorth)>> NetWorthAsync(CancellationToken ct) =>
        (await investments.NetWorthSnapshots.AsNoTracking().OrderBy(s => s.Date).ToListAsync(ct))
        .Select(s => (s.Date, s.AssetsBase, s.LiabilitiesBase, s.NetWorthBase)).ToList();
}
