namespace Ai.Contracts;

/// <param name="Write">True for the opt-in write scopes. Every write scope is also sensitive.</param>
public sealed record ScopeInfo(string Name, string Description, bool Sensitive, bool Write = false);

/// <summary>
/// Permissions an AI client can be granted. Read scopes are the default surface; sensitive scopes — including every
/// write scope — are off unless the owner explicitly grants them to a specific client (ADR-0008).
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

    // Write — opt-in per client, sensitive, never granted by default (ADR-0008).
    public const string TransactionsWrite = "transactions.write";
    public const string RecurringWrite = "recurring.write";
    public const string PlanningWrite = "planning.write";
    public const string HoldingsWrite = "holdings.write";

    /// <summary>The only scopes a write tool may declare. Adding one is a reviewed change (architecture test).</summary>
    public static readonly IReadOnlySet<string> WriteScopes =
        new HashSet<string>(StringComparer.Ordinal) { TransactionsWrite, RecurringWrite, PlanningWrite, HoldingsWrite };

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
        new(TransactionsWrite, "Create, edit and delete hand-entered expenses, income and transfers (deletions go to a 30-day recycle bin). Never broker, investment or estimated-interest rows.", true, true),
        new(RecurringWrite, "Confirm or skip pending recurring items; create and edit recurring expenses and income.", true, true),
        new(PlanningWrite, "Change category budget limits; create and update goals and add money to a goal.", true, true),
        new(HoldingsWrite, "Update hand-entered crypto (quantity, average price), add crypto rewards and record the value of manually valued assets and liabilities.", true, true),
    ];

    public static readonly IReadOnlySet<string> Names = All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
}
