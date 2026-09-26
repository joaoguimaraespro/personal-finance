using Investments.Application.Calculations;

namespace Investments.Tests;

public sealed class PortfolioSeriesTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static DateOnly D(int day) => new(2026, 3, day);

    [Fact]
    public void Deposit_on_a_day_without_a_valuation_is_not_counted_as_return()
    {
        // Friday 6th: 1 000. Deposit 500 on Saturday 7th. Monday 9th: 1 500 → flat market, 0 % return.
        var valuations = new[] { new AccountValuation(A, D(6), 1_000m), new AccountValuation(A, D(9), 1_500m) };
        var flows = new[] { new AccountFlow(A, D(7), 500m) };

        var series = PortfolioSeries.Build(valuations, flows, null, D(31));

        series.Points[1].NetFlow.ShouldBe(500m);
        Performance.TimeWeightedReturn(series.Points.Select(p => p.ToValuation()).ToList())
            .ShouldBe(0m);
    }

    [Fact]
    public void A_newly_connected_account_enters_as_an_inflow_not_a_gain()
    {
        var valuations = new[]
        {
            new AccountValuation(A, D(2), 10_000m),
            new AccountValuation(A, D(3), 10_100m),
            new AccountValuation(B, D(3), 5_000m), // broker B connected on the 3rd
            new AccountValuation(A, D(4), 10_100m),
            new AccountValuation(B, D(4), 5_050m),
        };

        var series = PortfolioSeries.Build(valuations, [], null, D(31));

        series.Points.Select(p => p.Value).ShouldBe([10_000m, 15_100m, 15_150m]);
        series.Points[1].EndOfDayFlow.ShouldBe(5_000m);
        var twr = Performance.TimeWeightedReturn(series.Points.Select(p => p.ToValuation()).ToList())!.Value;
        // 10 000 → 10 100 (+1 %), then 15 100 → 15 150 (+0.33 %): about 1.33 %, not +51 %.
        twr.ShouldBe(0.013344m, 0.00001m);
    }

    [Fact]
    public void Missing_valuations_carry_the_last_value_forward()
    {
        var valuations = new[]
        {
            new AccountValuation(A, D(2), 100m), new AccountValuation(B, D(2), 100m),
            new AccountValuation(A, D(3), 110m), // B has no valuation on the 3rd
            new AccountValuation(A, D(4), 110m), new AccountValuation(B, D(4), 100m),
        };

        var series = PortfolioSeries.Build(valuations, [], null, D(31));

        series.Points.Select(p => p.Value).ShouldBe([200m, 210m, 210m]);
    }

    [Fact]
    public void Window_starts_from_the_carried_value_and_xirr_flows_balance()
    {
        var valuations = new[]
        {
            new AccountValuation(A, D(1), 1_000m),
            new AccountValuation(A, D(10), 1_100m),
            new AccountValuation(A, D(20), 1_700m),
        };
        var flows = new[] { new AccountFlow(A, D(1), 1_000m), new AccountFlow(A, D(15), 500m) };

        var series = PortfolioSeries.Build(valuations, flows, D(10), D(31));

        series.Points[0].Value.ShouldBe(1_100m);
        series.Points[1].NetFlow.ShouldBe(500m);
        series.Points[1].CumulativeContributions.ShouldBe(1_500m);
        series.InvestorFlows.Select(f => f.Amount).ShouldBe([-1_100m, -500m, 1_700m]);
    }
}
