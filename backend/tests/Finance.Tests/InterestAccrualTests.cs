using Finance.Domain.Accounts;
using Finance.Domain.Interest;
using SharedKernel;

namespace Finance.Tests;

public sealed class InterestAccrualTests
{
    private static readonly DateOnly July1 = new(2026, 7, 1);
    private static readonly YearMonth July = new(2026, 7);
    private static readonly YearMonth August = new(2026, 8);

    private static AccrualInput Input(decimal opening, DateOnly through, IReadOnlyList<RatePeriod>? rates = null,
        Dictionary<DateOnly, decimal>? flows = null, DateOnly? openingOn = null,
        InterestPayout payout = InterestPayout.Monthly, IReadOnlySet<YearMonth>? settled = null) =>
        new(opening, openingOn ?? July1, flows ?? [], rates ?? [new RatePeriod(July1, 2.25m, 28m)], payout, through,
            settled ?? new HashSet<YearMonth>());

    private static decimal Net(decimal balanceDays, decimal rate, decimal withholding = 28m) =>
        Currency.Round(balanceDays * rate / 100m / 365m * (1 - withholding / 100m));

    [Fact]
    public void Constant_balance_accrues_tanb_over_365_net_of_28_percent_withholding()
    {
        var result = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31)));

        var july = result.ShouldHaveSingleItem();
        july.Month.ShouldBe(July);
        july.DaysAccrued.ShouldBe(31);
        july.Gross.ShouldBe(19.11m);  // 10 000 × 2.25 % / 365 × 31
        july.Net.ShouldBe(13.76m);    // × 0.72
        july.LastDay.ShouldBe(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void A_mid_month_inflow_counts_from_its_own_day_using_end_of_day_balances()
    {
        var flows = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 7, 16)] = 9_000m };

        var july = InterestAccrual.Compute(Input(1_000m, new DateOnly(2026, 7, 31), flows: flows)).Single();

        // 15 days at 1 000, 16 days (16th..31st) at 10 000.
        july.Net.ShouldBe(Net(1_000m * 15 + 10_000m * 16, 2.25m));
    }

    [Fact]
    public void A_rate_change_mid_month_splits_the_month_by_period()
    {
        RatePeriod[] rates = [new(July1, 2m, 28m), new(new DateOnly(2026, 7, 16), 3m, 28m)];

        var july = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates)).Single();

        var expectedGross = 10_000m * (0.02m * 15 + 0.03m * 16) / 365m;
        july.Gross.ShouldBe(Currency.Round(expectedGross));
        july.Net.ShouldBe(Currency.Round(expectedGross * 0.72m));
    }

    [Fact]
    public void Withholding_can_change_with_the_rate_period()
    {
        RatePeriod[] rates = [new(July1, 2m, 28m), new(new DateOnly(2026, 7, 16), 2m, 0m)];

        var july = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates)).Single();

        var daily = 10_000m * 0.02m / 365m;
        july.Net.ShouldBe(Currency.Round(daily * 15 * 0.72m + daily * 16));
    }

    [Fact]
    public void An_account_opened_mid_month_accrues_only_from_its_opening_day()
    {
        RatePeriod[] rates = [new(new DateOnly(2026, 1, 1), 2.25m, 28m)];

        var july = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates,
            openingOn: new DateOnly(2026, 7, 20))).Single();

        july.DaysAccrued.ShouldBe(12);
        july.Net.ShouldBe(Net(10_000m * 12, 2.25m));
    }

    [Fact]
    public void Accrual_starts_on_the_first_rate_period_not_before()
    {
        RatePeriod[] rates = [new(new DateOnly(2026, 7, 25), 2.25m, 28m)];

        var july = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates)).Single();

        july.DaysAccrued.ShouldBe(7);
    }

    [Fact]
    public void Zero_or_negative_balances_earn_nothing()
    {
        var flows = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2026, 7, 11)] = -1_500m, // 1 000 → −500 from the 11th
            [new DateOnly(2026, 7, 21)] = 500m,    // back to exactly 0 from the 21st
        };

        var july = InterestAccrual.Compute(Input(1_000m, new DateOnly(2026, 7, 31), flows: flows)).Single();

        july.DaysAccrued.ShouldBe(10);
        july.Net.ShouldBe(Net(1_000m * 10, 2.25m));
        InterestAccrual.Compute(Input(-50m, new DateOnly(2026, 7, 31))).Single().Net.ShouldBe(0m);
    }

    [Fact]
    public void A_zero_rate_period_stops_accrual()
    {
        RatePeriod[] rates = [new(July1, 2.25m, 28m), new(new DateOnly(2026, 7, 11), 0m, 28m)];

        var july = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates)).Single();

        july.DaysAccrued.ShouldBe(10);
    }

    [Fact]
    public void Weekends_accrue_and_the_current_month_runs_up_to_today()
    {
        // 2026-08-01 is a Saturday; through the 3rd = three calendar days.
        var result = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 8, 3)));

        result.Select(r => r.Month).ShouldBe([July, August]);
        result[1].DaysAccrued.ShouldBe(3);
        result[1].LastDay.ShouldBe(new DateOnly(2026, 8, 3));
    }

    [Fact]
    public void Monthly_payout_credits_the_estimate_at_month_end_so_next_month_earns_on_it()
    {
        var result = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 8, 31)));

        result[0].Net.ShouldBe(13.76m);
        result[1].Net.ShouldBe(Net((10_000m + 13.76m) * 31, 2.25m));
    }

    [Fact]
    public void Daily_payout_compounds_every_day()
    {
        var monthly = InterestAccrual.Compute(Input(100_000m, new DateOnly(2026, 7, 31))).Single();
        var daily = InterestAccrual.Compute(Input(100_000m, new DateOnly(2026, 7, 31),
            payout: InterestPayout.Daily)).Single();

        daily.Net.ShouldBeGreaterThan(monthly.Net);
        (daily.Net - monthly.Net).ShouldBeLessThan(0.20m);
    }

    [Fact]
    public void Leap_years_still_divide_by_365_so_29_february_earns_a_day()
    {
        // 3.65 % on 10 000 is exactly 1.00 a day before tax.
        RatePeriod[] rates = [new(new DateOnly(2028, 2, 1), 3.65m, 0m)];

        var feb = InterestAccrual.Compute(Input(10_000m, new DateOnly(2028, 2, 29), rates,
            openingOn: new DateOnly(2028, 2, 1))).Single();

        feb.DaysAccrued.ShouldBe(29);
        feb.Net.ShouldBe(29.00m);
    }

    [Fact]
    public void Settled_months_get_no_estimate_and_real_interest_flows_like_any_inflow()
    {
        var flows = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 7, 31)] = 12.50m }; // what the bank paid

        var result = InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 8, 31), flows: flows,
            settled: new HashSet<YearMonth> { July }));

        var august = result.ShouldHaveSingleItem();
        august.Month.ShouldBe(August);
        august.Net.ShouldBe(Net(10_012.50m * 31, 2.25m));
    }

    [Fact]
    public void Without_rates_or_before_the_start_nothing_accrues()
    {
        InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 7, 31), rates: [])).ShouldBeEmpty();
        InterestAccrual.Compute(Input(10_000m, new DateOnly(2026, 6, 30))).ShouldBeEmpty();
    }
}

public sealed class InterestReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static InterestMonth Estimated(decimal net)
    {
        var month = InterestMonth.Start(Guid.NewGuid(), new YearMonth(2026, 9), Now);
        month.UpdateEstimate(net, net / 0.72m, Guid.NewGuid());
        return month;
    }

    [Fact]
    public void Confirming_the_estimate_marks_it_confirmed()
    {
        var month = Estimated(13.76m);
        var real = Guid.NewGuid();

        month.Resolve(13.76m, real, Now);

        month.Status.ShouldBe(InterestMonthStatus.Confirmed);
        month.ActualAmount.ShouldBe(13.76m);
        month.TransactionId.ShouldBe(real);
        month.IsResolved.ShouldBeTrue();
    }

    [Fact]
    public void A_different_real_amount_corrects_it_and_later_recalculations_leave_it_alone()
    {
        var month = Estimated(13.76m);

        month.Resolve(13.12m, Guid.NewGuid(), Now);
        month.UpdateEstimate(99m, 99m, Guid.NewGuid());

        month.Status.ShouldBe(InterestMonthStatus.Corrected);
        month.ActualAmount.ShouldBe(13.12m);
        month.EstimatedAmount.ShouldBe(13.76m);
    }

    [Fact]
    public void Nothing_paid_resolves_without_a_transaction()
    {
        var month = Estimated(13.76m);

        month.Resolve(0m, null, Now);

        month.Status.ShouldBe(InterestMonthStatus.Corrected);
        month.TransactionId.ShouldBeNull();
    }

    [Theory]
    [InlineData(AccountKind.Savings, true)]
    [InlineData(AccountKind.Bank, true)]
    [InlineData(AccountKind.Cash, false)]
    [InlineData(AccountKind.CreditCard, false)]
    public void Only_savings_and_bank_accounts_take_a_rate(AccountKind kind, bool allowed)
    {
        var account = Account.Create("Revolut", kind, "EUR", 0m, new DateOnly(2026, 1, 1));

        var rate = AccountInterestRate.Create(account, new DateOnly(2026, 1, 1), 2.25m, null, Now);

        rate.IsSuccess.ShouldBe(allowed);
        if (allowed)
        {
            rate.Value.WithholdingPercent.ShouldBe(28m);
        }
    }

    [Fact]
    public void Rates_and_withholding_must_be_percentages()
    {
        var account = Account.Create("Revolut", AccountKind.Savings, "EUR", 0m, new DateOnly(2026, 1, 1));

        AccountInterestRate.Create(account, new DateOnly(2026, 1, 1), -1m, null, Now).IsFailure.ShouldBeTrue();
        AccountInterestRate.Create(account, new DateOnly(2026, 1, 1), 2m, 101m, Now).IsFailure.ShouldBeTrue();
    }
}
