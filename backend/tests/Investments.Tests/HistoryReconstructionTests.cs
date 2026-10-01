using Investments.Application.Calculations;
using Investments.Application.Portfolio;

namespace Investments.Tests;

public sealed class HistoryReconstructionTests
{
    private static readonly Guid Etf = Guid.NewGuid();
    private static readonly Guid Us = Guid.NewGuid();

    // March 2026: Mon 2 … Fri 6, Sat 7, Sun 8, Mon 9 … Fri 13.
    private static DateOnly D(int day) => new(2026, 3, day);

    private static decimal? EurOnly(string currency, DateOnly date) => currency == "EUR" ? 1m : null;

    private static ReconstructionInput Input(DateOnly from, DateOnly to, Dictionary<Guid, decimal> now,
        DateOnly anchor, decimal anchorCash, LedgerFill[] fills, LedgerCash[] cash,
        Dictionary<Guid, IReadOnlyList<PriceClose>> closes, EurPerUnit? fx = null) =>
        new(from, to, now, anchor, anchorCash, fills, cash, closes, fx ?? EurOnly);

    [Fact]
    public void Holdings_cash_and_value_follow_the_ledger_day_by_day()
    {
        // Mon 2: deposit 1 000. Tue 3: buy 5 @ 100 (cash −501 incl. 1 fee). Thu 5: buy 2 @ 110 (−220).
        // Today (Mon 9, first real snapshot) the broker reports 7 units and 279 cash.
        var fills = new[]
        {
            new LedgerFill(D(3), Etf, 5, 100m, "EUR", -501m),
            new LedgerFill(D(5), Etf, 2, 110m, "EUR", -220m),
        };
        var cash = new[] { new LedgerCash(D(2), 1_000m, true) };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>>
        {
            [Etf] = [new(D(2), 99m, "EUR"), new(D(3), 101m, "EUR"), new(D(4), 105m, "EUR"), new(D(5), 111m, "EUR"),
                new(D(6), 112m, "EUR")],
        };

        var days = HistoryReconstruction.Build(Input(D(2), D(8), new() { [Etf] = 7 }, D(9), 279m, fills, cash, closes));

        days.Select(d => d.Date).ShouldBe([D(2), D(3), D(4), D(5), D(6), D(7), D(8)]);
        days[0].ShouldBe(new ReconstructedDay(D(2), 0m, 1_000m, 1_000m, 0));
        days[1].ShouldBe(new ReconstructedDay(D(3), 505m, 499m, 0m, 0));
        days[2].MarketValue.ShouldBe(525m);
        days[3].ShouldBe(new ReconstructedDay(D(5), 777m, 279m, 0m, 0));
        // Weekend: Friday's close carries over.
        days[5].MarketValue.ShouldBe(784m);
        days[6].MarketValue.ShouldBe(784m);
        days.ShouldAllBe(d => d.EstimatedHoldings == 0);
    }

    [Fact]
    public void Foreign_listings_are_converted_at_each_days_rate()
    {
        var fills = new[] { new LedgerFill(D(2), Us, 10, 200m, "USD", -1_800m) };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>>
        {
            [Us] = [new(D(2), 200m, "USD"), new(D(3), 210m, "USD")],
        };
        decimal? Fx(string currency, DateOnly date) => currency switch
        {
            "EUR" => 1m,
            "USD" => date <= D(2) ? 0.9m : 0.8m,
            _ => null,
        };

        var days = HistoryReconstruction.Build(Input(D(2), D(3), new() { [Us] = 10 }, D(4), 200m,
            fills, [new LedgerCash(D(2), 2_000m, true)], closes, Fx));

        days[0].MarketValue.ShouldBe(1_800m); // 10 × 200 × 0.9
        days[1].MarketValue.ShouldBe(1_680m); // 10 × 210 × 0.8
    }

    [Fact]
    public void Pence_quotes_are_converted_to_pounds()
    {
        var table = new FxTable(new Dictionary<string, (DateOnly, decimal)[]> { ["GBP"] = [(D(2), 1.2m)] });

        table.EurPerUnit("GBX", D(5)).ShouldBe(0.012m);
        table.EurPerUnit("GBp", D(1)).ShouldBe(0.012m); // Before the first rate: the earliest one is used.
        table.EurPerUnit("EUR", D(5)).ShouldBe(1m);
        table.EurPerUnit("JPY", D(5)).ShouldBeNull();
    }

    [Fact]
    public void Without_a_public_close_the_last_trade_price_is_used_and_flagged()
    {
        var fills = new[]
        {
            new LedgerFill(D(2), Etf, 4, 50m, "EUR", -200m),
            new LedgerFill(D(10), Etf, 1, 60m, "EUR", -60m),
        };

        var days = HistoryReconstruction.Build(Input(D(2), D(11), new() { [Etf] = 5 }, D(12), 0m, fills,
            [new LedgerCash(D(2), 260m, true)], []));

        days[0].MarketValue.ShouldBe(200m);
        days[0].EstimatedHoldings.ShouldBe(1);
        days[7].MarketValue.ShouldBe(200m);  // Mon 9: still 4 units at the last trade price.
        days[8].MarketValue.ShouldBe(300m);  // Tue 10: 5 units at 60.
        days.ShouldAllBe(d => d.EstimatedHoldings == 1);
    }

    [Fact]
    public void A_stale_close_gives_way_to_a_more_recent_trade_price()
    {
        var fills = new[] { new LedgerFill(D(2), Etf, 1, 100m, "EUR", -100m), new LedgerFill(D(12), Etf, 1, 130m, "EUR", -130m) };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>> { [Etf] = [new(D(2), 101m, "EUR")] };

        var days = HistoryReconstruction.Build(Input(D(2), D(12), new() { [Etf] = 2 }, D(13), 0m, fills,
            [new LedgerCash(D(2), 230m, true)], closes));

        days[0].ShouldBe(new ReconstructedDay(D(2), 101m, 130m, 230m, 0));
        days[9].EstimatedHoldings.ShouldBe(1); // Wed 11: close is 9 days old → trade price of the 2nd, flagged.
        days[9].MarketValue.ShouldBe(100m);
        days[10].MarketValue.ShouldBe(260m);
    }

    [Fact]
    public void History_ends_where_the_real_data_starts_even_with_an_incomplete_ledger()
    {
        // The broker reports 10 units and 50 cash, but the ledger only knows one buy of 4 (older history is missing).
        var fills = new[] { new LedgerFill(D(4), Etf, 4, 100m, "EUR", -400m) };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>>
        {
            [Etf] = [new(D(2), 100m, "EUR"), new(D(3), 100m, "EUR"), new(D(4), 100m, "EUR"), new(D(5), 100m, "EUR")],
        };

        var days = HistoryReconstruction.Build(Input(D(2), D(5), new() { [Etf] = 10 }, D(6), 50m, fills, [], closes));

        days[^1].MarketValue.ShouldBe(1_000m); // Matches the real holdings on the first real day.
        days[^1].Cash.ShouldBe(50m);
        days[0].MarketValue.ShouldBe(600m);    // 6 units already held before the first known buy.
        days[0].Cash.ShouldBe(450m);
    }

    [Fact]
    public void Flows_after_the_anchor_do_not_shift_cash_but_later_fills_do_shift_holdings()
    {
        // Anchor (first real snapshot) on the 5th; a sale on the 9th is already reflected in today's positions.
        var fills = new[]
        {
            new LedgerFill(D(2), Etf, 3, 10m, "EUR", -30m),
            new LedgerFill(D(9), Etf, -1, 12m, "EUR", 12m),
        };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>> { [Etf] = [new(D(2), 10m, "EUR")] };

        var days = HistoryReconstruction.Build(Input(D(2), D(4), new() { [Etf] = 2 }, D(5), 70m, fills,
            [new LedgerCash(D(2), 100m, true), new LedgerCash(D(9), 500m, true)], closes));

        days.ShouldAllBe(d => d.MarketValue == 30m && d.Cash == 70m);
        days[0].NetFlow.ShouldBe(100m);
    }

    [Fact]
    public void Reconstructed_series_gives_correct_twr_with_deposits()
    {
        // Deposit 1 000 and buy 10 @ 100 on day 2; deposit 1 000 more and buy 10 @ 100 on day 4; price +10 % on day 5.
        var fills = new[]
        {
            new LedgerFill(D(2), Etf, 10, 100m, "EUR", -1_000m),
            new LedgerFill(D(4), Etf, 10, 100m, "EUR", -1_000m),
        };
        var closes = new Dictionary<Guid, IReadOnlyList<PriceClose>>
        {
            [Etf] = [new(D(2), 100m, "EUR"), new(D(3), 100m, "EUR"), new(D(4), 100m, "EUR"), new(D(5), 110m, "EUR")],
        };
        var days = HistoryReconstruction.Build(Input(D(2), D(5), new() { [Etf] = 20 }, D(6), 0m, fills,
            [new LedgerCash(D(2), 1_000m, true), new LedgerCash(D(4), 1_000m, true)], closes));

        var series = PortfolioSeries.Build(days.Select(d => new AccountValuation(Etf, d.Date, d.MarketValue + d.Cash)).ToList(),
            [new AccountFlow(Etf, D(2), 1_000m), new AccountFlow(Etf, D(4), 1_000m)], null, D(5));

        Performance.TimeWeightedReturn(series.Points.Select(p => p.ToValuation()).ToList()).ShouldBe(0.1m);
        series.Points[^1].CumulativeContributions.ShouldBe(2_000m);
    }
}
