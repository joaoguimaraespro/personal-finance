using SharedKernel;

namespace Finance.Domain.Goals;

/// <summary>
/// A savings target. Progress is, in order: the balance of the cash account it is linked to
/// (<see cref="AccountId"/>, e.g. a savings account kept for it — interest included), else the amount tracked by hand
/// (<see cref="ManualCurrentAmount"/>), else <see cref="StartingAmount"/> + savings transactions linked to the goal.
/// Investments never count: goals are about money set aside, not market value.
/// </summary>
public sealed class FinancialGoal : Entity, IAuditable
{
    private FinancialGoal() { }

    public string Name { get; private set; } = null!;
    public decimal TargetAmount { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public decimal StartingAmount { get; private set; }
    public decimal? ManualCurrentAmount { get; private set; }
    public string? Icon { get; private set; }

    /// <summary>A cash account (savings, bank, cash) whose balance is the goal's progress.</summary>
    public Guid? AccountId { get; private set; }
    public DateTimeOffset? AchievedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static FinancialGoal Create(string name, decimal targetAmount, DateOnly? targetDate, decimal startingAmount,
        decimal? manualCurrentAmount, string? icon) => new()
    {
        Name = name.Trim(),
        TargetAmount = targetAmount,
        TargetDate = targetDate,
        StartingAmount = startingAmount,
        ManualCurrentAmount = manualCurrentAmount,
        Icon = icon,
    };

    public void Update(string name, decimal targetAmount, DateOnly? targetDate, decimal startingAmount,
        decimal? manualCurrentAmount, string? icon)
    {
        Name = name.Trim();
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        StartingAmount = startingAmount;
        ManualCurrentAmount = manualCurrentAmount;
        Icon = icon;
    }

    public decimal CurrentAmount(decimal linkedContributions, decimal? accountBalance = null) =>
        AccountId is not null && accountBalance is { } balance
            ? Math.Max(balance, 0)
            : ManualCurrentAmount ?? StartingAmount + linkedContributions;

    public void LinkAccount(Guid? accountId) => AccountId = accountId;

    /// <summary>
    /// Records money set aside for the goal outside the ledger: it raises the amount tracked by hand when the goal
    /// is tracked that way, otherwise the starting amount (linked savings transactions keep adding on top).
    /// </summary>
    public void AddToCurrent(decimal amount)
    {
        if (ManualCurrentAmount is { } manual)
        {
            ManualCurrentAmount = manual + amount;
        }
        else
        {
            StartingAmount += amount;
        }
    }

    public void MarkAchieved(DateTimeOffset now) => AchievedAtUtc ??= now;

    public void Archive(DateTimeOffset now) => ArchivedAtUtc = now;
}
