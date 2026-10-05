using Investments.Application.Calculations;
using Investments.Application.Portfolio;

namespace Investments.Tests;

/// <summary>Return over 1M / YTD / 1Y: time-weighted %, gain in EUR net of deposits, partial history.</summary>
public sealed class PeriodReturnsTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Theory]
    [InlineData("1M", ReturnPeriod.OneMonth)]
    [InlineData("ytd", ReturnPeriod.YearToDate)]
    [InlineData("1Y", ReturnPeriod.OneYear)]
    [InlineData("ALL", ReturnPeriod.All)]
    [InlineData(null, ReturnPeriod.All)]
    [InlineData("5Y", ReturnPeriod.All)]
    public void Periods_parse_from_the_api_value(string? value, ReturnPeriod expected)
    {
        PeriodReturns.Parse(value).ShouldBe(expected);
        if (value is "1M" or "1Y" or "ALL")
        {
            PeriodReturns.Code(expected).ShouldBe(value);
        }
    }

    [Fact]
    public void Base_dates_are_a_month_or_a_year_back_and_31_december_for_the_year_to_date()
    {
        var today = D(2026, 10, 5);
        PeriodReturns.BaseDate(ReturnPeriod.OneMonth, today).ShouldBe(D(2026, 9, 5));
        PeriodReturns.BaseDate(ReturnPeriod.OneYear, today).ShouldBe(D(2025, 10, 5));
        PeriodReturns.BaseDate(ReturnPeriod.YearToDate, today).ShouldBe(D(2025, 12, 31));
        PeriodReturns.BaseDate(ReturnPeriod.All, today).ShouldBeNull();
        // 31 March minus a month is the last day of February.
        PeriodReturns.BaseDate(ReturnPeriod.OneMonth, D(2026, 3, 31)).ShouldBe(D(2026, 2, 28));
        // On 1 January the year to date is a single day, measured from 31 December.
        PeriodReturns.BaseDate(ReturnPeriod.YearToDate, D(2027, 1, 1)).ShouldBe(D(2026, 12, 31));
    }

    [Fact]
    public void A_deposit_inside_the_period_is_neither_gain_nor_return()
    {
        // 1 000 at the start, +10 % to 1 100, deposit 1 100 (2 200), +10 % to 2 420.
        var valuations = new[]
        {
            new AccountValuation(A, D(2026, 9, 5), 1_000m),
            new AccountValuation(A, D(2026, 9, 15), 1_100m),
            new AccountValuation(A, D(2026, 9, 20), 2_200m),
            new AccountValuation(A, D(2026, 10, 5), 2_420m),
        };
        var flows = new[] { new AccountFlow(A, D(2026, 9, 20), 1_100m) };

        var r = PeriodReturns.Compute(valuations, flows, D(2026, 9, 5), D(2026, 10, 5))!;

        r.From.ShouldBe(D(2026, 9, 5));
        r.Partial.ShouldBeFalse();
        r.NetFlows.ShouldBe(1_100m);
        r.Gain.ShouldBe(320m); // 2 420 − 1 000 − 1 100
        r.TimeWeightedReturn.ShouldBe(0.21m); // 1.1 × 1.1 − 1, not distorted by the deposit
    }

    [Fact]
    public void Flows_before_the_start_belong_to_the_starting_value()
    {
        var valuations = new[]
        {
            new AccountValuation(A, D(2025, 6, 1), 500m),
            new AccountValuation(A, D(2025, 12, 31), 1_000m),
            new AccountValuation(A, D(2026, 3, 1), 1_050m),
        };
        var flows = new[] { new AccountFlow(A, D(2025, 6, 1), 500m), new AccountFlow(A, D(2025, 9, 1), 400m) };

        var r = PeriodReturns.Compute(valuations, flows, D(2025, 12, 31), D(2026, 3, 1))!;

        r.StartValue.ShouldBe(1_000m);
        r.NetFlows.ShouldBe(0m);
        r.Gain.ShouldBe(50m);
        r.TimeWeightedReturn.ShouldBe(0.05m);
    }

    [Fact]
    public void Year_to_date_starts_from_the_last_value_of_the_previous_year()
    {
        // No valuation on 31 December (a weekend/holiday): the 30th's value stands for it, and 2 January's move
        // belongs to the year.
        var valuations = new[]
        {
            new AccountValuation(A, D(2025, 12, 29), 900m),
            new AccountValuation(A, D(2025, 12, 30), 1_000m),
            new AccountValuation(A, D(2026, 1, 2), 1_020m),
            new AccountValuation(A, D(2026, 2, 2), 1_071m),
        };
        var baseDate = PeriodReturns.BaseDate(ReturnPeriod.YearToDate, D(2026, 2, 2))!.Value;

        var r = PeriodReturns.Compute(valuations, [], baseDate, D(2026, 2, 2))!;

        r.From.ShouldBe(D(2025, 12, 30));
        r.Partial.ShouldBeFalse();
        r.Gain.ShouldBe(71m);
        r.TimeWeightedReturn.ShouldBe(0.071m);
    }

    [Fact]
    public void History_starting_mid_period_uses_what_exists_and_says_so()
    {
        // Asked for a year, history begins on 1 August with the first deposit.
        var valuations = new[]
        {
            new AccountValuation(A, D(2026, 8, 1), 1_000m),
            new AccountValuation(A, D(2026, 9, 1), 1_080m),
            new AccountValuation(A, D(2026, 10, 5), 1_134m),
        };
        var flows = new[] { new AccountFlow(A, D(2026, 8, 1), 1_000m) };

        var r = PeriodReturns.Compute(valuations, flows, D(2025, 10, 5), D(2026, 10, 5))!;

        r.Partial.ShouldBeTrue();
        r.From.ShouldBe(D(2026, 8, 1));
        r.Gain.ShouldBe(134m);
        r.TimeWeightedReturn.ShouldBe(0.134m);
    }

    [Fact]
    public void An_account_opened_during_the_period_enters_as_money_in_not_as_gain()
    {
        var valuations = new[]
        {
            new AccountValuation(A, D(2026, 9, 1), 10_000m),
            new AccountValuation(A, D(2026, 9, 10), 10_100m),
            new AccountValuation(B, D(2026, 9, 10), 5_000m), // a crypto wallet added on the 10th
            new AccountValuation(A, D(2026, 10, 1), 10_100m),
            new AccountValuation(B, D(2026, 10, 1), 5_500m),
        };

        var r = PeriodReturns.Compute(valuations, [], D(2026, 9, 1), D(2026, 10, 1))!;

        r.Partial.ShouldBeFalse();
        r.NetFlows.ShouldBe(5_000m);
        r.Gain.ShouldBe(600m); // 100 from A, 500 from B
        r.TimeWeightedReturn.ShouldBe(0.043444m); // 1.01 × (15 600 / 15 100) − 1
    }

    [Fact]
    public void Without_two_values_there_is_no_period_return()
    {
        PeriodReturns.Compute([], [], D(2026, 9, 1), D(2026, 10, 1)).ShouldBeNull();
        PeriodReturns.Compute([new AccountValuation(A, D(2026, 10, 1), 100m)], [], D(2026, 9, 1), D(2026, 10, 1))
            .ShouldBeNull();
    }

    [Fact]
    public void Live_values_replace_todays_snapshot_and_count_todays_deposit()
    {
        var today = D(2026, 10, 5);
        var snapshots = new[]
        {
            new AccountValuation(A, D(2026, 10, 4), 1_000m),
            new AccountValuation(A, today, 1_005m), // this morning's snapshot
            new AccountValuation(B, D(2026, 10, 4), 300m),
        };
        var live = new[] { new AccountValuation(A, today, 1_520m), new AccountValuation(B, today, 330m) };

        var valuations = PortfolioQueries.WithLiveValues(snapshots, live, today);

        valuations.Where(v => v.Date == today).Select(v => v.Value).Order().ShouldBe([330m, 1_520m]);
        var r = PeriodReturns.Compute(valuations, [new AccountFlow(A, today, 500m)], D(2026, 10, 4), today)!;
        r.Gain.ShouldBe(50m); // 1 850 − 1 300 − 500
    }
}
