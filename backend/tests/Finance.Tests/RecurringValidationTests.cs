using Finance.Application.Recurring;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;

namespace Finance.Tests;

public sealed class RecurringValidationTests
{
    private static readonly RecurringRequestValidator Validator = new();

    private static RecurringRequest Request(RecurrenceFrequency frequency, int? interval = null, int? dayOfMonth = null) =>
        new("Gym", TransactionType.Expense, 25m, "EUR", Guid.NewGuid(), frequency, new DateOnly(2026, 10, 1),
            interval, dayOfMonth, null, Guid.NewGuid(), null, null, null, null);

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, true)]
    [InlineData(RecurrenceFrequency.Daily, 15, true)]
    [InlineData(RecurrenceFrequency.Daily, 365, true)]
    [InlineData(RecurrenceFrequency.Daily, 0, false)]
    [InlineData(RecurrenceFrequency.Daily, 366, false)]
    [InlineData(RecurrenceFrequency.Weekly, 24, true)]
    [InlineData(RecurrenceFrequency.Weekly, 25, false)]
    [InlineData(RecurrenceFrequency.Monthly, 0, false)]
    [InlineData(RecurrenceFrequency.Monthly, 24, true)]
    [InlineData(RecurrenceFrequency.Monthly, 30, false)]
    [InlineData(RecurrenceFrequency.Yearly, 25, false)]
    public void Interval_bounds_depend_on_the_frequency(RecurrenceFrequency frequency, int interval, bool valid)
    {
        Validator.Validate(Request(frequency, interval)).IsValid.ShouldBe(valid);
    }

    [Fact]
    public void Day_of_month_is_rejected_for_daily_items()
    {
        var result = Validator.Validate(Request(RecurrenceFrequency.Daily, 15, dayOfMonth: 15));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RecurringRequest.DayOfMonth));
        Validator.Validate(Request(RecurrenceFrequency.Daily, 15)).IsValid.ShouldBeTrue();
        Validator.Validate(Request(RecurrenceFrequency.Monthly, 1, dayOfMonth: 15)).IsValid.ShouldBeTrue();
    }
}
