using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using SharedKernel;

namespace Finance.Domain.Recurring;

public enum RecurrenceFrequency
{
    Weekly = 0,
    Monthly = 1,
    Yearly = 2,
}

/// <summary>
/// Template for predictable transactions (salary, rent, subscriptions). It only ever *proposes*
/// <see cref="ExpectedTransaction"/>s; the user confirms, edits or skips each one.
/// </summary>
public sealed class RecurringTransaction : Entity, IAuditable
{
    private RecurringTransaction() { }

    public string Name { get; private set; } = null!;
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public Guid AccountId { get; private set; }
    public Guid? CounterAccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public ExpenseNature? Nature { get; private set; }
    public Guid? BucketId { get; private set; }
    public string? Description { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }
    public int Interval { get; private set; } = 1;

    /// <summary>For monthly/yearly: day of month (clamped to month length, so 31 means "last day").</summary>
    public int? DayOfMonth { get; private set; }
    public DateOnly StartOn { get; private set; }
    public DateOnly? EndOn { get; private set; }
    public DateOnly NextDueOn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static RecurringTransaction Create(RecurringDefinition def)
    {
        var r = new RecurringTransaction();
        r.Apply(def);
        r.NextDueOn = r.FirstOccurrenceOnOrAfter(def.StartOn);
        return r;
    }

    public void Update(RecurringDefinition def)
    {
        var scheduleChanged = def.Frequency != Frequency || def.Interval != Interval ||
                              def.DayOfMonth != DayOfMonth || def.StartOn != StartOn;
        Apply(def);
        if (scheduleChanged)
        {
            NextDueOn = FirstOccurrenceOnOrAfter(def.StartOn > NextDueOn ? def.StartOn : NextDueOn);
        }
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>Returns due dates up to <paramref name="horizon"/> and advances the schedule past them.</summary>
    public IReadOnlyList<DateOnly> TakeDueOccurrences(DateOnly horizon)
    {
        var due = new List<DateOnly>();
        while (IsActive && NextDueOn <= horizon && (EndOn is null || NextDueOn <= EndOn))
        {
            due.Add(NextDueOn);
            NextDueOn = Advance(NextDueOn);
        }

        return due;
    }

    public TransactionDraft ToDraft(DateOnly occurredOn, decimal? amountOverride = null) => new(
        Type, occurredOn, amountOverride ?? Amount, Currency, AccountId, CategoryId, Nature, CounterAccountId,
        BucketId, Description: Description ?? Name);

    private void Apply(RecurringDefinition def)
    {
        Name = def.Name.Trim();
        Type = def.Type;
        Amount = def.Amount;
        Currency = def.Currency;
        AccountId = def.AccountId;
        CounterAccountId = def.CounterAccountId;
        CategoryId = def.CategoryId;
        Nature = def.Type == TransactionType.Expense ? def.Nature ?? ExpenseNature.Fixed : null;
        BucketId = def.BucketId;
        Description = def.Description;
        Frequency = def.Frequency;
        Interval = Math.Max(1, def.Interval);
        DayOfMonth = def.Frequency == RecurrenceFrequency.Weekly ? null : def.DayOfMonth ?? def.StartOn.Day;
        StartOn = def.StartOn;
        EndOn = def.EndOn;
    }

    private DateOnly FirstOccurrenceOnOrAfter(DateOnly from)
    {
        if (Frequency == RecurrenceFrequency.Weekly)
        {
            var d = StartOn;
            while (d < from)
            {
                d = d.AddDays(7 * Interval);
            }

            return d;
        }

        var candidate = Anchor(StartOn.Year, StartOn.Month);
        if (candidate < StartOn)
        {
            candidate = Advance(candidate);
        }

        while (candidate < from)
        {
            candidate = Advance(candidate);
        }

        return candidate;
    }

    private DateOnly Advance(DateOnly current) => Frequency switch
    {
        RecurrenceFrequency.Weekly => current.AddDays(7 * Interval),
        RecurrenceFrequency.Monthly => AnchorFrom(current.AddMonths(Interval)),
        _ => AnchorFrom(current.AddYears(Interval)),
    };

    private DateOnly AnchorFrom(DateOnly d) => Anchor(d.Year, d.Month);

    private DateOnly Anchor(int year, int month) =>
        new(year, month, Math.Min(DayOfMonth ?? 1, DateTime.DaysInMonth(year, month)));
}

public sealed record RecurringDefinition(
    string Name,
    TransactionType Type,
    decimal Amount,
    string Currency,
    Guid AccountId,
    RecurrenceFrequency Frequency,
    DateOnly StartOn,
    int Interval = 1,
    int? DayOfMonth = null,
    DateOnly? EndOn = null,
    Guid? CategoryId = null,
    ExpenseNature? Nature = null,
    Guid? CounterAccountId = null,
    Guid? BucketId = null,
    string? Description = null);

public enum ExpectedStatus
{
    Pending = 0,
    Confirmed = 1,
    Skipped = 2,
}

/// <summary>A proposed occurrence awaiting user confirmation — never a financial record by itself.</summary>
public sealed class ExpectedTransaction : Entity
{
    private ExpectedTransaction() { }

    public Guid RecurringTransactionId { get; private set; }
    public DateOnly DueOn { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public ExpectedStatus Status { get; private set; }
    public Guid? TransactionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static ExpectedTransaction Propose(RecurringTransaction template, DateOnly dueOn, DateTimeOffset now) => new()
    {
        RecurringTransactionId = template.Id,
        DueOn = dueOn,
        Amount = template.Amount,
        Currency = template.Currency,
        CreatedAtUtc = now,
    };

    public void Confirm(Guid transactionId, DateTimeOffset now)
    {
        Status = ExpectedStatus.Confirmed;
        TransactionId = transactionId;
        ResolvedAtUtc = now;
    }

    public void Skip(DateTimeOffset now)
    {
        Status = ExpectedStatus.Skipped;
        ResolvedAtUtc = now;
    }
}
