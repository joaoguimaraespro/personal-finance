using SharedKernel;

namespace Finance.Domain.Goals;

/// <summary>
/// A savings target. Progress = <see cref="StartingAmount"/> + savings transactions linked to the goal,
/// unless the user tracks it by hand via <see cref="ManualCurrentAmount"/>.
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

    public decimal CurrentAmount(decimal linkedContributions) =>
        ManualCurrentAmount ?? StartingAmount + linkedContributions;

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
