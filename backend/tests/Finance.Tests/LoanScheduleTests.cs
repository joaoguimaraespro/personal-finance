using Finance.Domain.Loans;

namespace Finance.Tests;

/// <summary>French amortisation as Portuguese banks compute it: fixed and variable rates, early repayments.</summary>
public sealed class LoanScheduleTests
{
    private static readonly DateOnly First = new(2026, 1, 5);

    private static IReadOnlyList<Instalment> Plan(decimal principal, int months, params RatePeriod[] rates) =>
        LoanSchedule.Build(principal, First, months, rates, []);

    [Fact]
    public void A_fixed_rate_loan_has_the_textbook_instalment_and_ends_at_zero()
    {
        var plan = Plan(100_000m, 360, new RatePeriod(First, 3m));

        plan.Count.ShouldBe(360);
        plan[0].Payment.ShouldBe(421.60m);
        plan[0].Interest.ShouldBe(250.00m); // 100,000 × 3 % / 12
        plan[0].Principal.ShouldBe(171.60m);
        plan[^1].Balance.ShouldBe(0m);
        plan[^1].Date.ShouldBe(First.AddMonths(359));
        plan.Sum(i => i.Principal).ShouldBe(100_000m);
        plan.Take(359).ShouldAllBe(i => i.Payment == 421.60m);
    }

    [Fact]
    public void Without_interest_the_capital_is_split_evenly()
    {
        var plan = Plan(12_000m, 12, new RatePeriod(First, 0m));

        plan.ShouldAllBe(i => i.Payment == 1_000m && i.Interest == 0);
        plan[^1].Balance.ShouldBe(0m);
    }

    [Fact]
    public void A_rate_revision_recalculates_the_instalment_over_the_remaining_term()
    {
        var revision = First.AddMonths(12);
        var plan = Plan(100_000m, 360, new RatePeriod(First, 3m), new RatePeriod(revision, 4m));

        plan[11].Payment.ShouldBe(421.60m);
        var before = plan[11].Balance;
        plan[12].AnnualRatePercent.ShouldBe(4m);
        plan[12].Payment.ShouldBe(LoanSchedule.Annuity(before, 4m / 1200m, 348));
        plan[12].Payment.ShouldBeGreaterThan(421.60m);
        plan.Count.ShouldBe(360); // same end date
        plan[^1].Balance.ShouldBe(0m);
    }

    [Fact]
    public void Repaying_early_to_shorten_the_term_keeps_the_instalment_and_ends_sooner()
    {
        var rates = new[] { new RatePeriod(First, 3m) };
        var plan = LoanSchedule.Build(100_000m, First, 360, rates,
            [new Prepayment(First.AddMonths(11).AddDays(10), 20_000m, PrepaymentMode.ReduceTerm)]);

        plan[12].Prepaid.ShouldBe(20_000m);
        plan[12].Payment.ShouldBe(421.60m);
        plan.Count.ShouldBeLessThan(300);
        plan[^1].Balance.ShouldBe(0m);
        plan.Sum(i => i.Principal + i.Prepaid).ShouldBe(100_000m);
    }

    [Fact]
    public void Repaying_early_to_lower_the_instalment_keeps_the_end_date()
    {
        var rates = new[] { new RatePeriod(First, 3m) };
        var plan = LoanSchedule.Build(100_000m, First, 360, rates,
            [new Prepayment(First.AddMonths(11).AddDays(10), 20_000m, PrepaymentMode.ReducePayment)]);

        plan[12].Payment.ShouldBeLessThan(421.60m);
        plan.Count.ShouldBe(360);
        plan[^1].Balance.ShouldBe(0m);
    }

    [Fact]
    public void Status_reads_the_plan_on_a_day()
    {
        var plan = Plan(10_000m, 24, new RatePeriod(First, 5m));

        var status = LoanSchedule.StatusOn(plan, 10_000m, First.AddMonths(5)); // 6 instalments paid
        status.InstalmentsPaid.ShouldBe(6);
        status.InstalmentsLeft.ShouldBe(18);
        status.Outstanding.ShouldBe(plan[5].Balance);
        status.Next!.Date.ShouldBe(First.AddMonths(6));
        status.EndDate.ShouldBe(First.AddMonths(23));
        (status.InterestPaid + status.InterestLeft).ShouldBe(plan.Sum(i => i.Interest));

        LoanSchedule.StatusOn(plan, 10_000m, First.AddDays(-1)).Outstanding.ShouldBe(10_000m);
    }
}
