using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Categories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Reporting.Application.Calculations;
using Reporting.Application.Queries;
using SharedKernel;

namespace Reporting.Application;

public sealed record MonthlyComparison(
    MonthlySummary Current,
    MonthlySummary Previous,
    MonthlyAverage TrailingAverage);

/// <summary>Average of the months (up to 12, before the current one) that had any activity.</summary>
public sealed record MonthlyAverage(int Months, decimal Income, decimal TotalExpenses, decimal FixedExpenses,
    decimal VariableExpenses, decimal Invested, decimal Saved, decimal NetBalance, decimal? SavingsRate);

public sealed record CategoryLine(
    Guid CategoryId,
    string Key,
    string Name,
    Guid? ParentId,
    string? Color,
    ExpenseNature? Nature,
    decimal Actual,
    decimal? Budget,
    decimal? Variance,
    string Status,
    decimal PreviousMonth,
    decimal TrailingAverage);

public sealed record OverviewDto(
    int Year,
    decimal Income,
    decimal TotalExpenses,
    decimal Invested,
    decimal Saved,
    decimal NetBalance,
    decimal? AverageMonthlySavingsRate,
    decimal? WeightedSavingsRate,
    int PendingExpected,
    EverydayTotals Everyday,
    InvestmentTotals Investments);

/// <summary>Day-to-day money only: income and expenses. Investments are never part of these figures.</summary>
public sealed record EverydayTotals(decimal Income, decimal Expenses, decimal FixedExpenses, decimal VariableExpenses,
    decimal NetBalance);

/// <summary>Investing only: what was put into and taken out of investments, and the share of income invested.</summary>
public sealed record InvestmentTotals(decimal Purchases, decimal Sales, decimal NetInvested, decimal? InvestmentRate);

public sealed record TrendPoint(string Period, decimal Income, decimal Expenses, decimal Invested, decimal Saved,
    decimal NetBalance, decimal? SavingsRate);

public static class ReportEndpoints
{
    private static readonly Error BadPeriod = Error.Validation("Period", "Use yyyy-MM.");

    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/monthly/{period}", async (string period, LedgerAggregates ledger, CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(BadPeriod);
            }

            return Results.Ok(await MonthlyComparisonAsync(ledger, ym, ct));
        });

        group.MapGet("/annual/{year:int}", async (int year, LedgerAggregates ledger, CancellationToken ct) =>
        {
            if (year is < 1970 or > 2100)
            {
                return ResultHttp.Problem(Error.Validation("Year", "Year out of range."));
            }

            var months = await ledger.MonthlySummariesAsync(new YearMonth(year, 1), new YearMonth(year, 12), ct);
            return Results.Ok(AnnualCalculator.Compute(year, months));
        });

        group.MapGet("/overview/{year:int}", async (int year, LedgerAggregates ledger, IFinanceDb db,
            CancellationToken ct) =>
        {
            var months = await ledger.MonthlySummariesAsync(new YearMonth(year, 1), new YearMonth(year, 12), ct);
            var annual = AnnualCalculator.Compute(year, months);
            var pending = await db.ExpectedTransactions.CountAsync(
                e => e.Status == Finance.Domain.Recurring.ExpectedStatus.Pending, ct);
            var t = annual.Totals;
            return Results.Ok(new OverviewDto(year, t.Income, t.TotalExpenses, t.Invested, t.Saved, t.NetBalance,
                t.AverageMonthlySavingsRate, t.WeightedSavingsRate, pending,
                new EverydayTotals(t.Income, t.TotalExpenses, t.FixedExpenses, t.VariableExpenses, t.NetBalance),
                new InvestmentTotals(t.InvestmentPurchases, t.InvestmentSales, t.Invested, t.WeightedInvestmentRate)));
        });

        group.MapGet("/categories/{period}", async (string period, LedgerAggregates ledger, IFinanceDb db,
            CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(BadPeriod);
            }

            return Results.Ok(await CategoryBreakdownAsync(ledger, db, ym, ct));
        });

        group.MapGet("/trends", async (LedgerAggregates ledger, TimeProvider clock, string? to, int? months,
            CancellationToken ct) =>
        {
            var end = YearMonth.TryParse(to, out var parsed)
                ? parsed
                : YearMonth.From(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            var count = Math.Clamp(months ?? 12, 1, 60);
            var start = YearMonth.From(end.FirstDay.AddMonths(-(count - 1)));
            var summaries = await ledger.MonthlySummariesAsync(start, end, ct);
            return Results.Ok(summaries.Select(m => new TrendPoint(m.Period.ToString(), m.Income, m.TotalExpenses,
                m.Invested, m.Saved, m.NetBalance, m.SavingsRate)));
        });

        return app;
    }

    public static async Task<MonthlyComparison> MonthlyComparisonAsync(LedgerAggregates ledger, YearMonth ym,
        CancellationToken ct)
    {
        var start = YearMonth.From(ym.FirstDay.AddMonths(-12));
        var summaries = await ledger.MonthlySummariesAsync(start, ym, ct);
        var current = summaries[^1];
        var previous = summaries[^2];
        var history = summaries.Take(summaries.Count - 1).Where(m => m.TransactionCount > 0).ToList();
        return new MonthlyComparison(current, previous, Average(history));
    }

    public static async Task<IReadOnlyList<CategoryLine>> CategoryBreakdownAsync(LedgerAggregates ledger,
        IFinanceDb db, YearMonth ym, CancellationToken ct)
    {
        var totals = await ledger.MonthTotalsAsync(YearMonth.From(ym.FirstDay.AddMonths(-12)), ym, ct);
        var current = totals[^1];
        var previous = totals[^2];
        var history = totals.Take(totals.Count - 1).Where(t => t.TransactionCount > 0).ToList();
        var budget = await ledger.EffectiveBudgetAsync(ym, ct);
        var targets = BudgetTargets.Resolve(budget, current.Income);

        var categories = await db.Categories.AsNoTracking().Where(c => c.Type == CategoryType.Expense)
            .ToListAsync(ct);
        return categories
            .Where(c => current.ByCategory.ContainsKey(c.Id) || targets.Categories.ContainsKey(c.Id) ||
                        previous.ByCategory.ContainsKey(c.Id))
            .Select(c =>
            {
                var actual = current.ByCategory.GetValueOrDefault(c.Id);
                decimal? target = targets.Categories.TryGetValue(c.Id, out var v) ? v : null;
                var avg = history.Count == 0 ? 0 : history.Average(h => h.ByCategory.GetValueOrDefault(c.Id));
                var status = target switch
                {
                    null => "none",
                    _ when actual > target => "over",
                    _ when actual >= target * 0.9m => "near",
                    _ => "under",
                };
                return new CategoryLine(c.Id, c.Key, c.Name, c.ParentId, c.Color, c.DefaultNature, actual, target,
                    target is null ? null : target - actual, status, previous.ByCategory.GetValueOrDefault(c.Id),
                    Currency.Round(avg));
            })
            .OrderByDescending(l => l.Actual)
            .ToList();
    }

    private static MonthlyAverage Average(List<MonthlySummary> months)
    {
        if (months.Count == 0)
        {
            return new MonthlyAverage(0, 0, 0, 0, 0, 0, 0, 0, null);
        }

        decimal Avg(Func<MonthlySummary, decimal> f) => Currency.Round(months.Average(f));
        var rates = months.Where(m => m.SavingsRate is not null).Select(m => m.SavingsRate!.Value).ToList();
        return new MonthlyAverage(months.Count, Avg(m => m.Income), Avg(m => m.TotalExpenses),
            Avg(m => m.FixedExpenses), Avg(m => m.VariableExpenses), Avg(m => m.Invested), Avg(m => m.Saved),
            Avg(m => m.NetBalance), rates.Count == 0 ? null : decimal.Round(rates.Average(), 6));
    }
}
