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

    [McpServerTool(Name = "get_income_summary", Title = "Income summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Income for a month or a year, by category.")]
    public Task<string> GetIncomeSummary([Description("Month as yyyy-MM.")] string? period = null,
        [Description("Calendar year (instead of a month).")] int? year = null,
        CancellationToken ct = default) => Call("get_income_summary", ct, ("period", period), ("year", year));

    [McpServerTool(Name = "get_net_worth", Title = "Net worth", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Current net worth by group and its change since tracking started.")]
    public Task<string> GetNetWorth(CancellationToken ct = default) => Call("get_net_worth", ct);

    [McpServerTool(Name = "get_portfolio_summary", Title = "Portfolio summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Portfolio value, net contributions, total return, unrealised/realised P&L, dividends and fees.")]
    public Task<string> GetPortfolioSummary([Description("Optional broker: Trading212, InteractiveBrokers or Demo.")] string? broker = null,
        CancellationToken ct = default) => Call("get_portfolio_summary", ct, ("broker", broker));

    [McpServerTool(Name = "get_positions", Title = "Positions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Largest holdings with weight, value and P&L.")]
    public Task<string> GetPositions([Description("Optional broker filter.")] string? broker = null,
        [Description("Number of positions (1-25, default 10).")] int? top = null,
        CancellationToken ct = default) => Call("get_positions", ct, ("broker", broker), ("top", top));

    [McpServerTool(Name = "get_portfolio_performance", Title = "Portfolio performance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Time-weighted (cumulative) and money-weighted (XIRR, annualised) returns plus month-end values.")]
    public Task<string> GetPortfolioPerformance([Description("1y, 3y or all (default all).")] string? range = null,
        CancellationToken ct = default) => Call("get_portfolio_performance", ct, ("range", range));

    [McpServerTool(Name = "get_dividend_summary", Title = "Dividend summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Dividends received by month and by security.")]
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
