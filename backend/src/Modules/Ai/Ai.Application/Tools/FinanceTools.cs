using Ai.Application.Gateway;
using Ai.Contracts;
using Finance.Application.Abstractions;
using Finance.Application.Goals;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;
using Microsoft.EntityFrameworkCore;
using Reporting.Application;
using Reporting.Application.Calculations;
using Reporting.Application.Queries;
using SharedKernel;

namespace Ai.Application.Tools;

public sealed record ToolContext(IReadOnlySet<string> Scopes, ToolArgs Args, DateOnly Today)
{
    public bool Has(string scope) => Scopes.Contains(scope);
}

public sealed record ToolOutput(object Data, int RecordCount);

/// <summary>
/// Implementations of the AI tools. Each returns a purpose-built DTO containing only what answers its question:
/// asking about restaurant spending never returns accounts, other categories or the portfolio.
/// All numbers come from the same calculators as the application.
/// </summary>
public sealed class FinanceTools(IFinanceDb finance, LedgerAggregates ledger, PortfolioQueries portfolio,
    NetWorthService netWorth)
{
    public Task<ToolOutput> RunAsync(string tool, ToolContext ctx, CancellationToken ct) => tool switch
    {
        "get_financial_overview" => OverviewAsync(ctx, ct),
        "get_monthly_summary" => MonthlyAsync(ctx, ct),
        "get_expense_summary" => ExpenseSummaryAsync(ctx, ct),
        "get_expense_transactions" => ExpenseTransactionsAsync(ctx, ct),
        "get_income_summary" => IncomeAsync(ctx, ct),
        "get_net_worth" => NetWorthAsync(ctx, ct),
        "get_portfolio_summary" => PortfolioSummaryAsync(ctx, ct),
        "get_positions" => PositionsAsync(ctx, ct),
        "get_portfolio_performance" => PerformanceAsync(ctx, ct),
        "get_dividend_summary" => DividendsAsync(ctx, ct),
        "get_budget_status" => BudgetAsync(ctx, ct),
        "get_goals" => GoalsAsync(ctx, ct),
        _ => throw new ToolArgumentException("Unknown tool."),
    };

    private async Task<ToolOutput> OverviewAsync(ToolContext ctx, CancellationToken ct)
    {
        var year = ctx.Args.Year(ctx.Today);
        var months = await ledger.MonthlySummariesAsync(new YearMonth(year, 1), new YearMonth(year, 12), ct);
        var t = AnnualCalculator.Compute(year, months).Totals;
        return new(new
        {
            year,
            currency = Currency.Base,
            income = t.Income,
            expenses = t.TotalExpenses,
            fixedExpenses = t.FixedExpenses,
            variableExpenses = t.VariableExpenses,
            invested = t.Invested,
            saved = t.Saved,
            netBalance = t.NetBalance,
            averageMonthlySavingsRate = t.AverageMonthlySavingsRate,
            incomeWeightedSavingsRate = t.WeightedSavingsRate,
            monthsWithActivity = months.Count(m => m.TransactionCount > 0),
        }, 1);
    }

    private async Task<ToolOutput> MonthlyAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var r = await ReportEndpoints.MonthlyComparisonAsync(ledger, period, ct);
        object Shape(MonthlySummary m) => new
        {
            period = m.Period.ToString(), income = m.Income, fixedExpenses = m.FixedExpenses,
            variableExpenses = m.VariableExpenses, totalExpenses = m.TotalExpenses, invested = m.Invested, saved = m.Saved,
            netBalance = m.NetBalance, savingsRate = m.SavingsRate, expenseBudget = m.ExpenseBudget,
            expenseBudgetRemaining = m.ExpenseBudgetBalance,
        };
        return new(new { currency = Currency.Base, current = Shape(r.Current), previous = Shape(r.Previous) }, 2);
    }

    private async Task<ToolOutput> ExpenseSummaryAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var category = ctx.Args.Category();
        var lines = await ReportEndpoints.CategoryBreakdownAsync(ledger, finance, period, ct);
        if (category is not null)
        {
            var match = await ResolveCategoryAsync(category, ct);
            if (match is null)
            {
                return new(new { period = period.ToString(), category, found = false }, 0);
            }

            var ids = await finance.Categories.Where(c => c.Id == match.Value.Id || c.ParentId == match.Value.Id)
                .Select(c => c.Id).ToListAsync(ct);
            var selected = lines.Where(l => ids.Contains(l.CategoryId)).ToList();
            var count = await finance.Transactions.CountAsync(t => t.Type == TransactionType.Expense &&
                t.CategoryId != null && ids.Contains(t.CategoryId.Value) &&
                t.OccurredOn >= period.FirstDay && t.OccurredOn <= period.LastDay, ct);
            var own = selected.FirstOrDefault(l => l.CategoryId == match.Value.Id);
            return new(new
            {
                period = period.ToString(),
                currency = Currency.Base,
                category = match.Value.Name,
                total = selected.Sum(l => l.Actual),
                transactions = count,
                budget = own?.Budget,
                budgetRemaining = own?.Variance,
                budgetStatus = own?.Status,
                previousMonth = selected.Sum(l => l.PreviousMonth),
                twelveMonthAverage = selected.Sum(l => l.TrailingAverage),
            }, 1);
        }

        var top = lines.Where(l => l.Actual > 0).Take(30).ToList();
        return new(new
        {
            period = period.ToString(),
            currency = Currency.Base,
            totalExpenses = lines.Sum(l => l.Actual),
            byCategory = top.Select(l => new { category = l.Name, nature = l.Nature?.ToString(), total = l.Actual, budget = l.Budget, budgetStatus = l.Status }),
        }, top.Count);
    }

    private async Task<ToolOutput> ExpenseTransactionsAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var category = ctx.Args.Category();
        var limit = ctx.Args.Bounded("limit", 20, 1, 50);
        var query = finance.Transactions.AsNoTracking().Where(t => t.Type == TransactionType.Expense &&
            t.OccurredOn >= period.FirstDay && t.OccurredOn <= period.LastDay);
        if (category is not null)
        {
            var match = await ResolveCategoryAsync(category, ct);
            if (match is null)
            {
                return new(new { period = period.ToString(), category, found = false }, 0);
            }

            var ids = await finance.Categories.Where(c => c.Id == match.Value.Id || c.ParentId == match.Value.Id)
                .Select(c => c.Id).ToListAsync(ct);
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }

        var rows = await (from t in query
                join c in finance.Categories on t.CategoryId equals c.Id
                join a in finance.Accounts on t.AccountId equals a.Id
                orderby t.OccurredOn descending, t.CreatedAtUtc descending
                select new { t.OccurredOn, t.OriginalAmount, t.OriginalCurrency, t.BaseAmount, Category = c.Name, t.Description, t.Notes, Account = a.Institution ?? a.Name, t.Source })
            .Take(limit).ToListAsync(ct);

        var withAccount = ctx.Has(AiScopes.RawTransactions);
        var withNotes = ctx.Has(AiScopes.PersonalNotes);
        return new(new
        {
            period = period.ToString(),
            baseCurrency = Currency.Base,
            items = rows.Select(r => new
            {
                date = r.OccurredOn,
                amount = r.OriginalAmount,
                currency = r.OriginalCurrency,
                amountEur = r.BaseAmount,
                category = r.Category,
                description = UntrustedText.From(r.Description),
                account = withAccount ? r.Account : null,
                source = withAccount ? r.Source.ToString() : null,
                notes = withNotes ? UntrustedText.From(r.Notes) : null,
            }),
            truncated = rows.Count == limit,
        }, rows.Count);
    }

    private async Task<ToolOutput> IncomeAsync(ToolContext ctx, CancellationToken ct)
    {
        var (from, to, label) = ctx.Args.Has("year") && !ctx.Args.Has("period")
            ? (new YearMonth(ctx.Args.Year(ctx.Today), 1), new YearMonth(ctx.Args.Year(ctx.Today), 12), ctx.Args.Year(ctx.Today).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : (ctx.Args.Period(ctx.Today), ctx.Args.Period(ctx.Today), ctx.Args.Period(ctx.Today).ToString());
        var totals = await ledger.MonthTotalsAsync(from, to, ct);
        var names = await finance.Categories.AsNoTracking().Where(c => c.Type == CategoryType.Income)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var byCategory = totals.SelectMany(t => t.IncomeByCategory)
            .GroupBy(kv => kv.Key).Select(g => new { category = names.GetValueOrDefault(g.Key, "Other"), total = g.Sum(kv => kv.Value) })
            .OrderByDescending(x => x.total).ToList();
        return new(new { period = label, currency = Currency.Base, totalIncome = totals.Sum(t => t.Income), byCategory,
            note = "Broker dividends are reported separately by get_dividend_summary." }, byCategory.Count);
    }

    private async Task<ToolOutput> NetWorthAsync(ToolContext ctx, CancellationToken ct)
    {
        var h = await netWorth.HistoryAsync(ct);
        object? accounts = null;
        if (ctx.Has(AiScopes.AccountIdentifiers))
        {
            var list = await finance.Accounts.AsNoTracking().Where(a => a.ArchivedAtUtc == null).ToListAsync(ct);
            accounts = list.Select(a => new { name = UntrustedText.From(a.Name), kind = a.Kind.ToString(), institution = a.Institution, identifier = a.Identifier });
        }

        return new(new
        {
            asOf = h.Current.Date,
            currency = Currency.Base,
            netWorth = h.Current.NetWorth,
            assets = h.Current.Assets,
            liabilities = h.Current.Liabilities,
            cashAndBank = h.Current.Cash,
            investments = h.Current.Investments,
            otherAssets = h.Current.ManualAssets,
            changeSinceStart = h.ChangeSinceStart,
            changeSinceStartPercent = h.ChangeSinceStartPercent,
            trackingSince = h.StartDate,
            accounts,
        }, 1);
    }

    private async Task<ToolOutput> PortfolioSummaryAsync(ToolContext ctx, CancellationToken ct)
    {
        var s = await portfolio.SummaryAsync(Scope(ctx), ct);
        return new(new
        {
            currency = Currency.Base,
            totalValue = s.TotalValue, marketValue = s.MarketValue, cash = s.Cash, netContributions = s.NetContributions,
            totalReturn = s.TotalReturn, totalReturnPercent = s.TotalReturnPercent, unrealizedPnl = s.UnrealizedPnl,
            realizedPnl = s.RealizedPnl, dividendsNet = s.Dividends, feesAndTaxes = s.Fees, positions = s.Positions,
            brokers = s.Accounts.Select(a => a.Broker.ToString()).Distinct(),
            lastSyncUtc = s.LastSyncUtc,
        }, 1);
    }

    private async Task<ToolOutput> PositionsAsync(ToolContext ctx, CancellationToken ct)
    {
        var top = ctx.Args.Bounded("top", 10, 1, 25);
        var positions = (await portfolio.PositionsAsync(Scope(ctx), ct)).Take(top).ToList();
        return new(new
        {
            currency = Currency.Base,
            items = positions.Select(p => new
            {
                symbol = p.Symbol, name = UntrustedText.From(p.Name), isin = p.Isin, assetClass = p.AssetClass.ToString(),
                quantity = p.Quantity, priceCurrency = p.Currency, lastPrice = p.LastPrice, averagePrice = p.AveragePrice,
                marketValue = p.MarketValueBase, unrealizedPnl = p.UnrealizedPnlBase, unrealizedPnlPercent = p.UnrealizedPnlPercent,
                portfolioWeight = p.PortfolioWeight, brokers = p.Holdings.Select(h => h.Broker.ToString()).Distinct(),
            }),
        }, positions.Count);
    }

    private async Task<ToolOutput> PerformanceAsync(ToolContext ctx, CancellationToken ct)
    {
        var range = ctx.Args.OneOf("range", "1y", "3y", "all") ?? "all";
        DateOnly? from = range switch { "1y" => ctx.Today.AddYears(-1), "3y" => ctx.Today.AddYears(-3), _ => null };
        var p = await portfolio.PerformanceAsync(new PortfolioScope(), from, null, ct);
        var monthEnds = p.Series.GroupBy(s => (s.Date.Year, s.Date.Month)).Select(g => g.Last()).TakeLast(60)
            .Select(s => new { month = $"{s.Date:yyyy-MM}", value = s.Value, netContributions = s.NetContributions }).ToList();
        return new(new
        {
            currency = Currency.Base, from = p.From, to = p.To,
            timeWeightedReturnCumulative = p.TimeWeightedReturn, moneyWeightedReturnAnnualised = p.MoneyWeightedReturn,
            startValue = p.StartValue, endValue = p.EndValue, netContributions = p.NetFlows, gain = p.Gain, monthEnds,
        }, monthEnds.Count);
    }

    private async Task<ToolOutput> DividendsAsync(ToolContext ctx, CancellationToken ct)
    {
        var year = ctx.Args.Has("year") ? ctx.Args.Year(ctx.Today) : (int?)null;
        var d = await portfolio.DividendsAsync(new PortfolioScope(), year is { } y ? new DateOnly(y, 1, 1) : null,
            year is { } y2 ? new DateOnly(y2, 12, 31) : null, ct);
        return new(new
        {
            year, currency = Currency.Base, totalNet = d.TotalNetBase,
            byMonth = d.ByMonth.TakeLast(36).Select(m => new { month = m.Period, net = m.Amount }),
            bySecurity = d.BySecurity.Take(10).Select(s => new { symbol = s.Symbol, name = UntrustedText.From(s.Name), net = s.Amount }),
        }, d.ByMonth.Count);
    }

    private async Task<ToolOutput> BudgetAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var r = (await ReportEndpoints.MonthlyComparisonAsync(ledger, period, ct)).Current;
        var categories = (await ReportEndpoints.CategoryBreakdownAsync(ledger, finance, period, ct))
            .Where(l => l.Budget is not null).ToList();
        return new(new
        {
            period = period.ToString(),
            currency = Currency.Base,
            expenseBudget = r.ExpenseBudget,
            spent = r.TotalExpenses,
            expenseBudgetRemaining = r.ExpenseBudgetBalance,
            allocation = r.Buckets.Select(b => new { bucket = b.Name, type = b.IsInvestment ? "investment" : "savings", target = b.Target, actual = b.Actual, difference = b.Difference }),
            categoryLimits = categories.Select(c => new { category = c.Name, budget = c.Budget, spent = c.Actual, remaining = c.Variance, status = c.Status }),
        }, r.Buckets.Count + categories.Count);
    }

    private async Task<ToolOutput> GoalsAsync(ToolContext ctx, CancellationToken ct)
    {
        var goals = await GoalEndpoints.ListAsync(finance, ctx.Today, false, ct);
        return new(new
        {
            currency = Currency.Base,
            items = goals.Select(g => new
            {
                name = UntrustedText.From(g.Name), target = g.TargetAmount, current = g.CurrentAmount, progress = g.Progress,
                remaining = g.Remaining, targetDate = g.TargetDate, neededPerMonth = g.MonthlyNeeded, achieved = g.Achieved,
            }),
        }, goals.Count);
    }

    private static PortfolioScope Scope(ToolContext ctx) =>
        new(ctx.Args.OneOf("broker", "Trading212", "InteractiveBrokers", "Demo") is { } b ? Enum.Parse<DataSource>(b) : null);

    /// <summary>Matches a category by key or by name (built-in English or Portuguese names, or custom names).</summary>
    private async Task<(Guid Id, string Name)?> ResolveCategoryAsync(string input, CancellationToken ct)
    {
        var needle = input.Trim().ToLowerInvariant();
        var categories = await finance.Categories.AsNoTracking().Where(c => c.Type == CategoryType.Expense)
            .Select(c => new { c.Id, c.Key, c.Name }).ToListAsync(ct);
        var hit = categories.FirstOrDefault(c => c.Key == needle.Replace(' ', '-'))
                  ?? categories.FirstOrDefault(c => c.Name.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase))
                  ?? categories.FirstOrDefault(c => PortugueseAliases.TryGetValue(needle, out var key) && c.Key == key);
        return hit is null ? null : (hit.Id, hit.Name);
    }

    private static readonly Dictionary<string, string> PortugueseAliases = new()
    {
        ["restaurantes"] = "restaurants", ["supermercado"] = "groceries", ["habitação"] = "housing", ["habitacao"] = "housing",
        ["transportes"] = "transport", ["combustível"] = "fuel", ["combustivel"] = "fuel", ["viagens"] = "travel",
        ["lazer"] = "entertainment", ["subscrições"] = "subscriptions", ["subscricoes"] = "subscriptions", ["ginásio"] = "gym",
        ["roupa"] = "clothing", ["saúde"] = "health", ["saude"] = "health", ["educação"] = "education", ["educacao"] = "education",
        ["seguros"] = "insurance", ["impostos"] = "taxes", ["prendas"] = "gifts", ["eletricidade"] = "electricity",
        ["alimentação"] = "food", ["alimentacao"] = "food", ["compras"] = "shopping", ["outro"] = "other",
    };
}
