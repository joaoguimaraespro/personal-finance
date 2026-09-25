using Finance.Domain.Recurring;
using Finance.Domain.Transactions;

namespace Finance.Tests;

public sealed class RecurringTests
{
    private static RecurringTransaction Monthly(int day, DateOnly start) => RecurringTransaction.Create(
        new RecurringDefinition("Spotify", TransactionType.Expense, 10.99m, "EUR", Guid.NewGuid(),
            RecurrenceFrequency.Monthly, start, DayOfMonth: day, CategoryId: Guid.NewGuid()));

    [Fact]
    public void Monthly_on_the_25th_proposes_each_month_once()
    {
        var r = Monthly(25, new DateOnly(2026, 7, 1));

        r.NextDueOn.ShouldBe(new DateOnly(2026, 7, 25));
        r.TakeDueOccurrences(new DateOnly(2026, 9, 30))
            .ShouldBe([new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 25)]);
        r.TakeDueOccurrences(new DateOnly(2026, 9, 30)).ShouldBeEmpty();
        r.NextDueOn.ShouldBe(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public void Day_31_clamps_to_the_last_day_of_short_months()
    {
        var r = Monthly(31, new DateOnly(2026, 1, 1));

        r.TakeDueOccurrences(new DateOnly(2026, 4, 30))
            .ShouldBe([new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)]);
    }

    [Fact]
    public void Start_after_the_anchor_day_begins_next_month()
    {
        Monthly(5, new DateOnly(2026, 9, 10)).NextDueOn.ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public void End_date_and_pause_stop_proposals()
    {
        var r = RecurringTransaction.Create(new RecurringDefinition("Gym", TransactionType.Expense, 30m, "EUR",
            Guid.NewGuid(), RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1), DayOfMonth: 1,
            EndOn: new DateOnly(2026, 2, 15), CategoryId: Guid.NewGuid()));

        r.TakeDueOccurrences(new DateOnly(2026, 12, 31)).Count.ShouldBe(2);

        var paused = Monthly(1, new DateOnly(2026, 1, 1));
        paused.SetActive(false);
        paused.TakeDueOccurrences(new DateOnly(2026, 12, 31)).ShouldBeEmpty();
    }

    [Fact]
    public void Weekly_and_yearly_schedules_advance_correctly()
    {
        var weekly = RecurringTransaction.Create(new RecurringDefinition("Cleaning", TransactionType.Expense, 20m,
            "EUR", Guid.NewGuid(), RecurrenceFrequency.Weekly, new DateOnly(2026, 9, 1), Interval: 2,
            CategoryId: Guid.NewGuid()));
        var yearly = RecurringTransaction.Create(new RecurringDefinition("Car insurance", TransactionType.Expense,
            300m, "EUR", Guid.NewGuid(), RecurrenceFrequency.Yearly, new DateOnly(2026, 3, 15),
            CategoryId: Guid.NewGuid()));

        weekly.TakeDueOccurrences(new DateOnly(2026, 9, 30))
            .ShouldBe([new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 29)]);
        yearly.TakeDueOccurrences(new DateOnly(2028, 12, 31))
            .ShouldBe([new DateOnly(2026, 3, 15), new DateOnly(2027, 3, 15), new DateOnly(2028, 3, 15)]);
    }
}
