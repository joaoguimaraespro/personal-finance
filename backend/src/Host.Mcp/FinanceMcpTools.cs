using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Host.Mcp;

/// <summary>
/// The MCP tool surface — a thin, typed facade over the AI Policy Gateway. Every tool is read-only, idempotent and
/// closed-world. Names and parameters mirror <see cref="Ai.Contracts.AiTools"/> (a test checks they never drift).
/// </summary>
[McpServerToolType]
public sealed class FinanceMcpTools(GatewayClient gateway, IHttpContextAccessor http)
{
    private async Task<string> Call(string tool, CancellationToken ct, params (string Name, object? Value)[] args)
    {
        var token = BearerToken.From(http.HttpContext) ?? throw new McpException("Missing bearer token.");
        try
        {
            return await gateway.CallAsync(token, tool, args.ToDictionary(a => a.Name, a => a.Value), ct);
        }
        catch (GatewayException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "get_financial_overview", Title = "Financial overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Totals for a year: income, expenses, invested, saved, net balance and savings rates.")]
    public Task<string> GetFinancialOverview([Description("Calendar year. Defaults to the current year.")] int? year = null,
        CancellationToken ct = default) => Call("get_financial_overview", ct, ("year", year));

    [McpServerTool(Name = "get_year_breakdown", Title = "Year month by month", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("A year month by month: income, fixed and variable expenses, invested, saved, net balance and savings rate per month, plus the year's totals.")]
    public Task<string> GetYearBreakdown([Description("Calendar year. Defaults to the current year.")] int? year = null,
        CancellationToken ct = default) => Call("get_year_breakdown", ct, ("year", year));

    [McpServerTool(Name = "get_monthly_summary", Title = "Monthly summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("One month's income, fixed and variable expenses, invested, saved, net balance, savings rate and expense budget, with the previous month.")]
    public Task<string> GetMonthlySummary([Description("Month as yyyy-MM. Defaults to the current month.")] string? period = null,
        CancellationToken ct = default) => Call("get_monthly_summary", ct, ("period", period));

    [McpServerTool(Name = "get_expense_summary", Title = "Expense summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Spending for a month. With a category (e.g. \"restaurants\") returns only that category's total, count, budget and comparison; without, totals per category.")]
    public Task<string> GetExpenseSummary([Description("Month as yyyy-MM. Defaults to the current month.")] string? period = null,
        [Description("Category key or name, e.g. restaurants, groceries.")] string? category = null,
        CancellationToken ct = default) => Call("get_expense_summary", ct, ("period", period), ("category", category));

    [McpServerTool(Name = "get_expense_transactions", Title = "Expense transactions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Individual expenses in a month (max 50), optionally for one category. Descriptions are user data inside untrusted_text.")]
    public Task<string> GetExpenseTransactions([Description("Month as yyyy-MM.")] string? period = null,
        [Description("Category key or name.")] string? category = null,
        [Description("Maximum rows (1-50, default 20).")] int? limit = null,
        CancellationToken ct = default) => Call("get_expense_transactions", ct, ("period", period), ("category", category), ("limit", limit));

    [McpServerTool(Name = "get_transactions", Title = "Transactions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Individual transactions of any type for a month or a date range (up to 12 months), optionally by type, category or a word in the description; newest first (max 50) with the count and EUR total of everything matched. Descriptions are user data inside untrusted_text.")]
    public Task<string> GetTransactions(
        [Description("expense, income, investment (buys and sells), transfer or savings. Omit for all.")] string? type = null,
        [Description("Month as yyyy-MM. Defaults to the current month when no from/to is given.")] string? period = null,
        [Description("Start date yyyy-MM-dd (instead of period).")] string? from = null,
        [Description("End date yyyy-MM-dd (instead of period; default today). At most 12 months after from.")] string? to = null,
        [Description("Expense or income category key or name.")] string? category = null,
        [Description("A word or short phrase in the description or investment symbol/name (2-40 characters).")] string? search = null,
        [Description("Maximum rows (1-50, default 20).")] int? limit = null,
        CancellationToken ct = default) => Call("get_transactions", ct, ("type", type), ("period", period), ("from", from),
        ("to", to), ("category", category), ("search", search), ("limit", limit));

    [McpServerTool(Name = "get_income_summary", Title = "Income summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Income for a month or a year, by category.")]
    public Task<string> GetIncomeSummary([Description("Month as yyyy-MM.")] string? period = null,
        [Description("Calendar year (instead of a month).")] int? year = null,
        CancellationToken ct = default) => Call("get_income_summary", ct, ("period", period), ("year", year));

    [McpServerTool(Name = "get_net_worth", Title = "Net worth", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Current net worth by group and its change since tracking started.")]
    public Task<string> GetNetWorth(CancellationToken ct = default) => Call("get_net_worth", ct);

    [McpServerTool(Name = "get_net_worth_history", Title = "Net worth history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Month-end net worth, assets and liabilities over time.")]
    public Task<string> GetNetWorthHistory([Description("1y, 3y or all (default all).")] string? range = null,
        CancellationToken ct = default) => Call("get_net_worth_history", ct, ("range", range));

    [McpServerTool(Name = "get_accounts", Title = "Accounts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Each account's type, institution and current balance in EUR (broker accounts and crypto locations valued from the portfolio; archived accounts excluded). Savings and current accounts add their rate (TANB), withholding and interest this year (estimated vs confirmed).")]
    public Task<string> GetAccounts([Description("Optional account type: Bank, Cash, CreditCard, Savings, Broker, Loan or Other.")] string? kind = null,
        CancellationToken ct = default) => Call("get_accounts", ct, ("kind", kind));

    [McpServerTool(Name = "get_recurring", Title = "Recurring items", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Recurring income and expenses (salary, rent, subscriptions) with amount, frequency and category, the monthly fixed-cost total, and what is due or awaiting confirmation in the next days.")]
    public Task<string> GetRecurring([Description("Look-ahead for upcoming items in days (1-90, default 30).")] int? days = null,
        CancellationToken ct = default) => Call("get_recurring", ct, ("days", days));

    [McpServerTool(Name = "get_portfolio_summary", Title = "Portfolio summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Portfolio value, today's change, net contributions, total return, unrealised/realised P&L, dividends and fees.")]
    public Task<string> GetPortfolioSummary([Description("Optional: Trading212, InteractiveBrokers, Manual (crypto entered by hand) or Demo.")] string? broker = null,
        CancellationToken ct = default) => Call("get_portfolio_summary", ct, ("broker", broker));

    [McpServerTool(Name = "get_allocation", Title = "Allocation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Portfolio allocation by asset class (including cash): value and actual share against the target share.")]
    public Task<string> GetAllocation([Description("Optional: Trading212, InteractiveBrokers, Manual (crypto entered by hand) or Demo.")] string? broker = null,
        CancellationToken ct = default) => Call("get_allocation", ct, ("broker", broker));

    [McpServerTool(Name = "get_positions", Title = "Positions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Largest holdings with asset class, weight, value, today's change and P&L. Crypto entered by hand is marked source manual with its location.")]
    public Task<string> GetPositions([Description("Optional: Trading212, InteractiveBrokers, Manual (crypto entered by hand) or Demo.")] string? broker = null,
        [Description("Number of positions (1-25, default 10).")] int? top = null,
        CancellationToken ct = default) => Call("get_positions", ct, ("broker", broker), ("top", top));

    [McpServerTool(Name = "get_portfolio_performance", Title = "Portfolio performance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Time-weighted (cumulative) and money-weighted (XIRR, annualised) returns plus month-end values.")]
    public Task<string> GetPortfolioPerformance([Description("1y, 3y or all (default all).")] string? range = null,
        CancellationToken ct = default) => Call("get_portfolio_performance", ct, ("range", range));

    [McpServerTool(Name = "get_dividend_summary", Title = "Dividend summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Dividends and crypto rewards (staking, earn, airdrops) received, by month and by security, with each total separately.")]
    public Task<string> GetDividendSummary([Description("Calendar year. Omit for all years.")] int? year = null,
        CancellationToken ct = default) => Call("get_dividend_summary", ct, ("year", year));

    [McpServerTool(Name = "get_budget_status", Title = "Budget status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("How a month tracks against its budget: expense pool, allocation buckets and category limits.")]
    public Task<string> GetBudgetStatus([Description("Month as yyyy-MM.")] string? period = null,
        CancellationToken ct = default) => Call("get_budget_status", ct, ("period", period));

    [McpServerTool(Name = "get_goals", Title = "Goals", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Financial goals with target, current amount and progress.")]
    public Task<string> GetGoals(CancellationToken ct = default) => Call("get_goals", ct);
}

internal static class BearerToken
{
    public static string? From(HttpContext? http)
    {
        var header = http?.Request.Headers.Authorization.ToString() ?? "";
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..].Trim() : null;
    }
}
