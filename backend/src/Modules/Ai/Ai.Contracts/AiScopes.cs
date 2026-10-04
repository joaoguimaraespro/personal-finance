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
    public const string AccountBalances = "accounts.balances.read";
    public const string Recurring = "recurring.read";
    public const string Transactions = "transactions.read";

    // Sensitive — never granted by default.
    public const string AccountIdentifiers = "accounts.identifiers.read";
    public const string RawTransactions = "raw.transactions.read";
    public const string PersonalNotes = "personal.notes.read";

    public static readonly IReadOnlyList<ScopeInfo> All =
    [
        new(Overview, "Monthly and annual totals (and a year month by month): income, expenses, invested, saved, savings rate.", false),
        new(ExpensesSummary, "Spending totals per category, budgets and variance.", false),
        new(ExpensesTransactions, "Individual expenses: date, amount, category and description (no account, no notes).", false),
        new(IncomeSummary, "Income totals per category.", false),
        new(Budget, "Budget targets and how the month is tracking against them.", false),
        new(Goals, "Financial goals and progress.", false),
        new(NetWorth, "Net worth totals by group (cash, investments, other assets, liabilities) and their month-end history.", false),
        new(PortfolioSummary, "Portfolio totals: value, today's change, contributions, return, dividends, fees, and allocation by asset class vs target.", false),
        new(PortfolioPositions, "Holdings: security, asset class, weight, value, today's change and P&L; crypto entered by hand shows its location (no broker account details).", false),
        new(PortfolioPerformance, "Time- and money-weighted returns and a monthly value series.", false),
        new(Dividends, "Dividends and crypto rewards received, by month and security.", false),
        new(AccountBalances, "Accounts by type with balance in EUR (brokers and crypto locations valued from the portfolio); savings rate (TANB) and interest this year. No account names or IBANs.", false),
        new(Recurring, "Recurring items (salary, rent, subscriptions): amount, frequency, category, monthly fixed costs and what is due soon.", false),
        new(Transactions, "Individual transactions of any type (expense, income, investment, transfer, savings): date, amount, category and description (no account, no notes).", false),
        new(AccountIdentifiers, "Account names and IBAN / account numbers in net-worth and account answers.", true),
        new(RawTransactions, "Adds the account and source of each transaction.", true),
        new(PersonalNotes, "Adds your personal notes on transactions.", true),
    ];

    public static readonly IReadOnlySet<string> Names = All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
}
