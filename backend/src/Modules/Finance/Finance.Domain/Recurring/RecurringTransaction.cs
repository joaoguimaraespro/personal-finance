using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using SharedKernel;

namespace Finance.Domain.Recurring;

public enum RecurrenceFrequency
{
    Weekly = 0,
    Monthly = 1,
    Yearly = 2,

    /// <summary>Every <c>Interval</c> days from <c>StartOn</c> (e.g. a gym billed every 15 days).</summary>
    Daily = 3,
}

/// <summary>
/// Template for predictable transactions (salary, rent, subscriptions). It only ever *proposes*
/// <see cref="ExpectedTransaction"/>s; the user confirms, edits or skips each one.
/// </summary>
public sealed class RecurringTransaction : Entity, IAuditable
{
    private readonly List<RecurringSplit> _splits = [];

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

    /// <summary>Largest <see cref="Interval"/> for <see cref="RecurrenceFrequency.Daily"/> (a year of days).</summary>
    public const int MaxDailyInterval = 365;

    /// <summary>Largest <see cref="Interval"/> for weekly, monthly and yearly items.</summary>
    public const int MaxInterval = 24;

    /// <summary>For monthly/yearly: day of month (clamped to month length, so 31 means "last day").</summary>
    public int? DayOfMonth { get; private set; }
    public DateOnly StartOn { get; private set; }
    public DateOnly? EndOn { get; private set; }
    public DateOnly NextDueOn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Category lines (e.g. one energy bill split into electricity and gas); empty for a single category.</summary>
    public IReadOnlyList<RecurringSplit> Splits => _splits;

    public static RecurringTransaction Create(RecurringDefinition def)
    {
        var r = new RecurringTransaction();
        r.Apply(def);
        r.NextDueOn = r.FirstOccurrenceOnOrAfter(def.StartOn);
        return r;
    }

    /// <summary>
    /// Applies <paramref name="def"/>. When the schedule changes (frequency, interval, day, start or end), upcoming
    /// occurrences are recalculated from the new anchor starting at <paramref name="today"/>: past occurrences are
    /// never re-proposed, and the caller drops the pending proposals dated today or later so they can be regenerated.
    /// </summary>
    /// <returns>Whether the schedule changed.</returns>
    public bool Update(RecurringDefinition def, DateOnly today)
    {
        var before = (Frequency, Interval, DayOfMonth, StartOn, EndOn);
        Apply(def);
        var scheduleChanged = before != (Frequency, Interval, DayOfMonth, StartOn, EndOn);
        if (scheduleChanged)
        {
            NextDueOn = FirstOccurrenceOnOrAfter(StartOn > today ? StartOn : today);
        }

        return scheduleChanged;
    }

    /// <summary>
    /// The first scheduled occurrence on or after <paramref name="today"/> (whether or not it was already proposed),
    /// or <c>null</c> when the schedule ends before then.
    /// </summary>
    public DateOnly? NextOccurrenceOnOrAfter(DateOnly today)
    {
        var next = FirstOccurrenceOnOrAfter(today);
        return EndOn is { } end && next > end ? null : next;
    }

    /// <summary>
    /// Average amount per month: weeks and days use 365-day years (<c>amount × 365 / (days between occurrences × 12)</c>),
    /// months and years divide by the number of months between occurrences.
    /// </summary>
    public static decimal MonthlyEquivalent(decimal amount, RecurrenceFrequency frequency, int interval)
    {
        var every = Math.Max(1, interval);
        return frequency switch
        {
            RecurrenceFrequency.Daily => amount * 365m / (every * 12m),
            RecurrenceFrequency.Weekly => amount * 365m / (7m * every * 12m),
            RecurrenceFrequency.Monthly => amount / every,
            _ => amount / (12m * every),
        };
    }

    public decimal MonthlyEquivalent() => MonthlyEquivalent(Amount, Frequency, Interval);

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

    /// <summary>
    /// The transaction this template proposes. Split lines are carried over; when the amount is adjusted they are
    /// rescaled proportionally (cents, remainder on the last line).
    /// </summary>
    public TransactionDraft ToDraft(DateOnly occurredOn, decimal? amountOverride = null)
    {
        var amount = amountOverride ?? Amount;
        return new(Type, occurredOn, amount, Currency, AccountId, CategoryId, Nature, CounterAccountId,
            BucketId, Description: Description ?? Name, Splits: SplitLinesFor(amount));
    }

    /// <summary>The split lines for an occurrence of <paramref name="amount"/>; null when the template is not split.</summary>
    public IReadOnlyList<SplitLine>? SplitLinesFor(decimal amount) => _splits.Count == 0
        ? null
        : SplitRules.Rescale(_splits.OrderBy(s => s.Position).Select(s => new SplitLine(s.CategoryId, s.Amount, s.Note)).ToList(),
            Amount, amount);

    private void Apply(RecurringDefinition def)
    {
        Name = def.Name.Trim();
        Type = def.Type;
        Amount = def.Amount;
        Currency = def.Currency;
        AccountId = def.AccountId;
        CounterAccountId = def.CounterAccountId;
        var split = def.Splits is { Count: > 0 };
        CategoryId = split ? null : def.CategoryId;
        // Split templates keep only an explicit nature: each line otherwise takes its category's default.
        Nature = def.Type == TransactionType.Expense ? split ? def.Nature : def.Nature ?? ExpenseNature.Fixed : null;
        _splits.Clear();
        if (split)
        {
            _splits.AddRange(def.Splits!.Select((l, i) => RecurringSplit.Create(i, l)));
        }

        BucketId = def.BucketId;
        Description = def.Description;
        Frequency = def.Frequency;
        Interval = Math.Max(1, def.Interval);
        // Day of month only anchors monthly and yearly items; weekly and daily ones count days from StartOn.
        DayOfMonth = IsDayBased(def.Frequency) ? null : def.DayOfMonth ?? def.StartOn.Day;
        StartOn = def.StartOn;
        EndOn = def.EndOn;
    }

    private DateOnly FirstOccurrenceOnOrAfter(DateOnly from)
    {
        if (IsDayBased(Frequency))
        {
            // Occurrences are StartOn + k × step days (k = 0, 1, …): pure date arithmetic, so DST never moves one.
            if (from <= StartOn)
            {
                return StartOn;
            }

            var step = StepDays();
            var periods = (from.DayNumber - StartOn.DayNumber + step - 1) / step;
            return StartOn.AddDays(periods * step);
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
        RecurrenceFrequency.Weekly or RecurrenceFrequency.Daily => current.AddDays(StepDays()),
        RecurrenceFrequency.Monthly => AnchorFrom(current.AddMonths(Interval)),
        _ => AnchorFrom(current.AddYears(Interval)),
    };

    private static bool IsDayBased(RecurrenceFrequency frequency) =>
        frequency is RecurrenceFrequency.Weekly or RecurrenceFrequency.Daily;

    private int StepDays() => Frequency == RecurrenceFrequency.Weekly ? 7 * Interval : Interval;

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
    string? Description = null,
    IReadOnlyList<SplitLine>? Splits = null);

/// <summary>A category line of a split recurring template. Amounts are in the template's currency.</summary>
public sealed class RecurringSplit
{
    private RecurringSplit() { }

    public Guid Id { get; private set; }
    public int Position { get; private set; }
    public Guid CategoryId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }

    internal static RecurringSplit Create(int position, SplitLine line) => new()
    {
        Id = Guid.CreateVersion7(),
        Position = position,
        CategoryId = line.CategoryId,
        Amount = line.Amount,
        Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
    };
}

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
