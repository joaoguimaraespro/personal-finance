using SharedKernel;

namespace Finance.Domain.Accounts;

public enum AccountKind
{
    Bank = 0,
    Cash = 1,
    CreditCard = 2,
    Savings = 3,
    Broker = 4,
    Loan = 5,
    Other = 6,
}

public sealed class Account : Entity, IAuditable
{
    private Account() { }

    public string Name { get; private set; } = null!;
    public AccountKind Kind { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public decimal OpeningBalance { get; private set; }
    public DateOnly OpeningBalanceOn { get; private set; }

    /// <summary>Institution name shown to AI clients instead of identifiers (e.g. "Revolut").</summary>
    public string? Institution { get; private set; }

    /// <summary>IBAN / account number. Encrypted at rest; only exposed under the sensitive scope.</summary>
    public string? Identifier { get; private set; }

    /// <summary>Broker accounts are fed by read-only integrations and cannot be edited manually.</summary>
    public bool IsManual => Kind != AccountKind.Broker;

    public bool IsLiability => Kind is AccountKind.CreditCard or AccountKind.Loan;
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static Account Create(string name, AccountKind kind, string currency, decimal openingBalance,
        DateOnly openingBalanceOn, string? institution = null, string? identifier = null) => new()
    {
        Name = name.Trim(),
        Kind = kind,
        Currency = currency,
        OpeningBalance = openingBalance,
        OpeningBalanceOn = openingBalanceOn,
        Institution = institution?.Trim(),
        Identifier = identifier?.Trim(),
    };

    public void Update(string name, decimal openingBalance, DateOnly openingBalanceOn, string? institution,
        string? identifier)
    {
        Name = name.Trim();
        OpeningBalance = openingBalance;
        OpeningBalanceOn = openingBalanceOn;
        Institution = institution?.Trim();
        Identifier = identifier?.Trim();
    }

    public void Archive(DateTimeOffset now) => ArchivedAtUtc = now;

    public void Restore() => ArchivedAtUtc = null;
}
