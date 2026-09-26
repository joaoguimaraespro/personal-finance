using Investments.Application.Calculations;

namespace Investments.Tests;

public sealed class PerformanceTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void Twr_ignores_the_size_and_timing_of_deposits()
    {
        // Day 1: 1 000 → +10 % → 1 100. Day 2: deposit 10 000 at start, market flat. Day 3: +10 %.
        var points = new[]
        {
            new ValuationPoint(D(2026, 1, 1), 1_000m, 1_000m),
            new ValuationPoint(D(2026, 1, 2), 1_100m, 0m),
            new ValuationPoint(D(2026, 1, 3), 11_100m, 10_000m),
            new ValuationPoint(D(2026, 1, 4), 12_210m, 0m),
        };

        // (1.1 × 1.0 × 1.1) − 1 = 21 % regardless of the big deposit.
        Performance.TimeWeightedReturn(points).ShouldBe(0.21m);
    }

    [Fact]
    public void Twr_needs_at_least_two_points() =>
        Performance.TimeWeightedReturn([new ValuationPoint(D(2026, 1, 1), 100m, 100m)]).ShouldBeNull();

    [Fact]
    public void Xirr_of_one_year_ten_percent_is_ten_percent()
    {
        var flows = new[] { new CashFlow(D(2025, 1, 1), -1_000m), new CashFlow(D(2026, 1, 1), 1_100m) };

        Performance.Xirr(flows)!.Value.ShouldBe(0.1m, 0.0001m);
    }

    [Fact]
    public void Xirr_matches_the_spreadsheet_reference_example()
    {
        // Microsoft's documented XIRR example → 0.373362535 (37.34 %).
        var flows = new[]
        {
            new CashFlow(D(2008, 1, 1), -10_000m),
            new CashFlow(D(2008, 3, 1), 2_750m),
            new CashFlow(D(2008, 10, 30), 4_250m),
            new CashFlow(D(2009, 2, 15), 3_250m),
            new CashFlow(D(2009, 4, 1), 2_750m),
        };

        Performance.Xirr(flows)!.Value.ShouldBe(0.373363m, 0.00001m);
    }

    [Fact]
    public void Xirr_handles_losses()
    {
        var flows = new[] { new CashFlow(D(2025, 1, 1), -1_000m), new CashFlow(D(2026, 1, 1), 800m) };

        Performance.Xirr(flows)!.Value.ShouldBe(-0.2m, 0.0001m);
    }

    [Fact]
    public void Xirr_is_undefined_without_both_signs() =>
        Performance.Xirr([new CashFlow(D(2025, 1, 1), -1_000m), new CashFlow(D(2026, 1, 1), -10m)]).ShouldBeNull();
}
