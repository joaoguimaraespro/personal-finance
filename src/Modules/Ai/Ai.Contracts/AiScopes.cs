namespace Ai.Contracts;

public sealed record ScopeInfo(string Name, string Description, bool Sensitive);

/// <summary>
/// Permissions an AI client can be granted. Everything is read-only; there are no write scopes. Sensitive scopes
/// are off unless the owner explicitly grants them to a specific client.
/// </summary>
public static class AiScopes
{
    public const string Overview = "overview.read";
    public const string ExpensesSummary = "expenses.summary.read";
    public const string ExpensesTransactions = "expenses.transactions.read";
    public const string IncomeSummary = "income.summary.read";
    public const string Budget = "budget.read";
    public const string Goals = "goals.read";
    public const string NetWorth = "networth.read";
    public const string PortfolioSummary = "portfolio.summary.read";
    public const string PortfolioPositions = "portfolio.positions.read";
    public const string PortfolioPerformance = "portfolio.performance.read";
    public const string Dividends = "dividends.read";

    // Sensitive — never granted by default.
    public const string AccountIdentifiers = "accounts.identifiers.read";
    public const string RawTransactions = "raw.transactions.read";
    public const string PersonalNotes = "personal.notes.read";

    public static readonly IReadOnlyList<ScopeInfo> All =
    [
        new(Overview, "Monthly and annual totals: income, expenses, invested, saved, savings rate.", false),
        new(ExpensesSummary, "Spending totals per category, budgets and variance.", false),
        new(ExpensesTransactions, "Individual expenses: date, amount, category and description (no account, no notes).", false),
        new(IncomeSummary, "Income totals per category.", false),
        new(Budget, "Budget targets and how the month is tracking against them.", false),
        new(Goals, "Financial goals and progress.", false),
        new(NetWorth, "Net worth totals by group (cash, investments, other assets, liabilities).", false),
        new(PortfolioSummary, "Portfolio totals: value, contributions, return, dividends, fees.", false),
        new(PortfolioPositions, "Holdings: security, weight, value and P&L (no broker account details).", false),
        new(PortfolioPerformance, "Time- and money-weighted returns and a monthly value series.", false),
        new(Dividends, "Dividends received, by month and security.", false),
        new(AccountIdentifiers, "Account names and IBAN / account numbers in net-worth answers.", true),
        new(RawTransactions, "Adds the account and source of each transaction.", true),
        new(PersonalNotes, "Adds your personal notes on transactions.", true),
    ];

    public static readonly IReadOnlySet<string> Names = All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
}
