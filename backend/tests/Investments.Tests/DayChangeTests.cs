using Investments.Application.Calculations;

namespace Investments.Tests;

public sealed class DayChangeTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public void Previous_close_is_the_latest_price_before_the_current_price_day()
    {
        var closes = new[] { (Monday.AddDays(-3), 100m), (Monday.AddDays(-4), 98m), (Monday, 103m) };

        // Monday's price compares with Friday, not with Monday's own earlier record.
        DayChange.PreviousClose(closes, Monday).ShouldBe(100m);
    }

    [Fact]
    public void Without_an_earlier_day_there_is_no_change()
    {
        var closes = new[] { (Monday, 103m) };

        var previous = DayChange.PreviousClose(closes, Monday);

        previous.ShouldBeNull();
        DayChange.Amount(10m, 103m, previous, 1m).ShouldBeNull();
        DayChange.Percent(null, 1030m).ShouldBeNull();
    }

    [Fact]
    public void Amount_is_converted_to_eur_and_percent_is_relative_to_yesterday()
    {
        // 10 shares, 100 → 110 USD, 1 USD = 0.9 EUR.
        var change = DayChange.Amount(10m, 110m, 100m, 0.9m);

        change.ShouldBe(90m);
        DayChange.Percent(change, 990m).ShouldBe(0.1m);
    }

    [Fact]
    public void A_loss_is_negative()
    {
        DayChange.Amount(4m, 95m, 100m, 1m).ShouldBe(-20m);
        DayChange.Percent(-20m, 380m).ShouldBe(-0.05m);
    }

    [Fact]
    public void Over_the_weekend_shares_show_fridays_move()
    {
        var thursday = new DateOnly(2026, 10, 1);
        var friday = thursday.AddDays(1);
        var saturday = friday.AddDays(1);
        var sunday = friday.AddDays(2);
        // Syncs keep recording the unchanged Friday price on Saturday and Sunday.
        var closes = new[] { (thursday, 100m), (friday, 104m), (saturday, 104m), (sunday, 104m) };

        var day = DayChange.TradingDay(sunday, tradesEveryDay: false);

        day.ShouldBe(friday);
        DayChange.PreviousClose(closes, day).ShouldBe(100m);
        DayChange.Amount(10m, 104m, DayChange.PreviousClose(closes, day), 1m).ShouldBe(40m);
    }

    [Fact]
    public void Crypto_and_weekdays_keep_their_own_date()
    {
        var sunday = new DateOnly(2026, 10, 4);
        var tuesday = new DateOnly(2026, 10, 6);

        DayChange.TradingDay(sunday, tradesEveryDay: true).ShouldBe(sunday);
        DayChange.TradingDay(tuesday, tradesEveryDay: false).ShouldBe(tuesday);
        DayChange.TradingDay(sunday.AddDays(-1), tradesEveryDay: false).DayOfWeek.ShouldBe(DayOfWeek.Friday);
    }
}
