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

    private static RecurringDefinition EveryNDays(int interval, DateOnly start, DateOnly? end = null) => new(
        "Gym", TransactionType.Expense, 25m, "EUR", Guid.Empty, RecurrenceFrequency.Daily, start, Interval: interval,
        EndOn: end, CategoryId: Guid.Empty);

    [Fact]
    public void Every_15_days_is_anchored_on_the_start_date()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)));

        r.NextDueOn.ShouldBe(new DateOnly(2026, 10, 1));
        r.TakeDueOccurrences(new DateOnly(2026, 11, 30)).ShouldBe([
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 31),
            new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 30),
        ]);
        r.NextDueOn.ShouldBe(new DateOnly(2026, 12, 15));
    }

    [Fact]
    public void Every_day_and_every_365_days()
    {
        RecurringTransaction.Create(EveryNDays(1, new DateOnly(2026, 2, 27)))
            .TakeDueOccurrences(new DateOnly(2026, 3, 2))
            .ShouldBe([new DateOnly(2026, 2, 27), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2)]);
        // Counts days, not years: 2028 is a leap year, so 365 days later is the day before the anniversary.
        RecurringTransaction.Create(EveryNDays(365, new DateOnly(2028, 1, 10)))
            .TakeDueOccurrences(new DateOnly(2029, 12, 31))
            .ShouldBe([new DateOnly(2028, 1, 10), new DateOnly(2029, 1, 9)]);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-25")] // Europe/Lisbon leaves summer time on 2026-10-25
    [InlineData("2027-03-13", "2027-03-28")] // ...and enters it again on 2027-03-28
    public void Occurrences_on_daylight_saving_changes_keep_their_date(string start, string dstDay)
    {
        var first = DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        var dst = DateOnly.Parse(dstDay, System.Globalization.CultureInfo.InvariantCulture);
        var r = RecurringTransaction.Create(EveryNDays(15, first));

        r.TakeDueOccurrences(dst.AddDays(15)).ShouldBe([first, dst, dst.AddDays(15)]);
        r.NextOccurrenceOnOrAfter(dst).ShouldBe(dst);
        r.NextOccurrenceOnOrAfter(dst.AddDays(-1)).ShouldBe(dst);
    }

    [Fact]
    public void End_date_mid_cycle_stops_daily_occurrences()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1), end: new DateOnly(2026, 11, 7)));

        r.TakeDueOccurrences(new DateOnly(2027, 12, 31))
            .ShouldBe([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 31)]);
        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 11, 1)).ShouldBeNull();
    }

    [Fact]
    public void Next_occurrence_is_the_first_on_or_after_today()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)));

        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 9, 1)).ShouldBe(new DateOnly(2026, 10, 1));
        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 10, 1)).ShouldBe(new DateOnly(2026, 10, 1));
        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 10, 2)).ShouldBe(new DateOnly(2026, 10, 16));
        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 10, 16)).ShouldBe(new DateOnly(2026, 10, 16));
        r.NextOccurrenceOnOrAfter(new DateOnly(2026, 10, 17)).ShouldBe(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void Changing_the_interval_recalculates_only_future_occurrences()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)));
        r.TakeDueOccurrences(new DateOnly(2026, 10, 20))
            .ShouldBe([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16)]);

        r.Update(EveryNDays(14, new DateOnly(2026, 10, 1)), today: new DateOnly(2026, 10, 20)).ShouldBeTrue();

        // Same anchor, every 14 days; 10-15 is in the past, so it is not proposed again.
        r.NextDueOn.ShouldBe(new DateOnly(2026, 10, 29));
        r.TakeDueOccurrences(new DateOnly(2026, 11, 30))
            .ShouldBe([new DateOnly(2026, 10, 29), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 26)]);
    }

    [Fact]
    public void Changing_the_start_moves_the_anchor()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)));
        r.TakeDueOccurrences(new DateOnly(2026, 10, 5));

        r.Update(EveryNDays(15, new DateOnly(2026, 10, 3)), today: new DateOnly(2026, 10, 5)).ShouldBeTrue();

        r.TakeDueOccurrences(new DateOnly(2026, 11, 3))
            .ShouldBe([new DateOnly(2026, 10, 18), new DateOnly(2026, 11, 2)]);
    }

    [Fact]
    public void Editing_only_the_amount_keeps_the_schedule()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)));
        r.TakeDueOccurrences(new DateOnly(2026, 10, 5));

        r.Update(EveryNDays(15, new DateOnly(2026, 10, 1)) with { Amount = 30m }, today: new DateOnly(2026, 10, 5))
            .ShouldBeFalse();
        r.NextDueOn.ShouldBe(new DateOnly(2026, 10, 16));
    }

    [Fact]
    public void Daily_items_ignore_a_day_of_month()
    {
        var r = RecurringTransaction.Create(EveryNDays(15, new DateOnly(2026, 10, 1)) with { DayOfMonth = 20 });

        r.DayOfMonth.ShouldBeNull();
        r.NextDueOn.ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Theory]
    [InlineData(1, 760.416667)] // 25 × 365 / 12
    [InlineData(7, 108.630952)] // 25 × 365 / 84
    [InlineData(15, 50.694444)] // 25 × 365 / 180
    [InlineData(30, 25.347222)] // 25 × 365 / 360
    public void Every_n_days_monthly_equivalent_uses_365_day_years(int days, double expected)
    {
        decimal.Round(RecurringTransaction.MonthlyEquivalent(25m, RecurrenceFrequency.Daily, days), 6)
            .ShouldBe((decimal)expected);
    }

    [Fact]
    public void Monthly_equivalent_of_other_frequencies()
    {
        // Weekly is the same as every 7 × interval days.
        RecurringTransaction.MonthlyEquivalent(25m, RecurrenceFrequency.Weekly, 1)
            .ShouldBe(RecurringTransaction.MonthlyEquivalent(25m, RecurrenceFrequency.Daily, 7));
        RecurringTransaction.MonthlyEquivalent(25m, RecurrenceFrequency.Weekly, 2)
            .ShouldBe(RecurringTransaction.MonthlyEquivalent(25m, RecurrenceFrequency.Daily, 14));
        RecurringTransaction.MonthlyEquivalent(30m, RecurrenceFrequency.Monthly, 3).ShouldBe(10m);
        RecurringTransaction.MonthlyEquivalent(240m, RecurrenceFrequency.Yearly, 2).ShouldBe(10m);
    }
}
