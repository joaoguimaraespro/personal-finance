using System.Net;
using System.Text;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Prices;
using Investments.Domain;
using Investments.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;

namespace Investments.Tests;

/// <summary>Coins entered by hand: valuation, rewards, the day's change and Yahoo crypto listings.</summary>
public sealed class ManualCryptoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 15, 30, 0, TimeSpan.Zero); // A Sunday.

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Yahoo", name));

    [Fact]
    public void Rewards_add_coins_at_zero_cost()
    {
        var holding = ManualHolding.Create(Guid.NewGuid(), Guid.NewGuid(), 2m, 30_000m, new DateOnly(2024, 1, 1),
            "  cold wallet  ", Now);
        holding.AddReward(new DateOnly(2026, 9, 1), 0.5m, RewardKind.Staking, null, Now);

        holding.Notes.ShouldBe("cold wallet");
        holding.Cost.ShouldBe(60_000m);
        holding.TotalQuantity.ShouldBe(2.5m);
        holding.AverageCostIncludingRewards.ShouldBe(24_000m);

        // The position the portfolio reads: gain over the price paid, rewards included at zero cost.
        var position = Position.Report(holding.AccountId, holding.SecurityId, holding.TotalQuantity,
            holding.AverageCostIncludingRewards, 40_000m, Now, DataSource.Manual, Now);
        position.MarketValue.ShouldBe(100_000m);
        position.CostBasis.ShouldBe(60_000m);
        (position.MarketValue - position.CostBasis).ShouldBe(40_000m);
    }

    [Fact]
    public void Editing_replaces_the_holding_and_removing_a_reward_takes_its_coins_back()
    {
        var holding = ManualHolding.Create(Guid.NewGuid(), Guid.NewGuid(), 1m, 100m, new DateOnly(2025, 1, 1), null,
            Now);
        var reward = holding.AddReward(new DateOnly(2026, 9, 1), 0.25m, RewardKind.Airdrop, "drop", Now);

        holding.Update(holding.AccountId, 3m, 50m, new DateOnly(2025, 6, 1), "", Now);
        holding.Cost.ShouldBe(150m);
        holding.Notes.ShouldBeNull();
        holding.TotalQuantity.ShouldBe(3.25m);

        holding.RemoveReward(reward.Id, Now).ShouldBeTrue();
        holding.RemoveReward(reward.Id, Now).ShouldBeFalse();
        holding.TotalQuantity.ShouldBe(3m);
        holding.AverageCostIncludingRewards.ShouldBe(50m);
    }

    [Fact]
    public void Reward_income_uses_the_close_of_the_day_it_was_received()
    {
        (DateOnly, decimal)[] closes =
        [
            (new DateOnly(2026, 9, 1), 100m),
            (new DateOnly(2026, 9, 2), 110m),
            (new DateOnly(2026, 9, 20), 200m),
        ];

        ManualCrypto.RewardValue(0.5m, new DateOnly(2026, 9, 2), closes, 999m).ShouldBe(55m);
        // A missing day uses the last close within a week…
        ManualCrypto.RewardValue(0.5m, new DateOnly(2026, 9, 5), closes, 999m).ShouldBe(55m);
        // …and without one, the latest known price.
        ManualCrypto.RewardValue(0.5m, new DateOnly(2026, 9, 15), closes, 999m).ShouldBe(499.5m);
    }

    [Fact]
    public void Crypto_trades_every_day_so_the_previous_close_is_yesterdays()
    {
        (DateOnly Date, decimal Close)[] closes =
        [
            (new DateOnly(2026, 10, 2), 60_000m), // Friday
            (new DateOnly(2026, 10, 3), 62_000m), // Saturday
            (new DateOnly(2026, 10, 4), 63_240m), // Sunday, live
        ];

        var priceAsOf = ManualCrypto.PriceAsOf(new DateOnly(2026, 10, 4), Now);
        priceAsOf.ShouldBe(Now);
        var previous = DayChange.PreviousClose(closes, DateOnly.FromDateTime(priceAsOf.UtcDateTime));
        previous.ShouldBe(62_000m);

        var change = DayChange.Amount(0.5m, 63_240m, previous, 1m);
        change.ShouldBe(620m);
        DayChange.Percent(change, 0.5m * 63_240m).ShouldBe(0.02m);
    }

    [Fact]
    public void The_24_hour_reference_is_the_price_of_the_bar_trading_24_hours_before_the_latest_quote()
    {
        // Recorded BTC-EUR 15-minute bars (interval=15m&range=2d), trimmed around the 24-hour mark.
        var quote = YahooParser.Rolling24h(Fixture("chart-intraday-BTC-EUR.json"))!;

        quote.Currency.ShouldBe("EUR");
        quote.LatestAtUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1791192388)); // Mon 5 Oct 09:26:28 UTC
        quote.LatestPrice.ShouldBe(76_567.3m);
        quote.ReferenceAtUtc.ShouldBe(quote.LatestAtUtc.AddHours(-24));
        // The 09:15 bar was trading at 09:26:28 the day before: its open.
        quote.ReferencePrice.ShouldBe(75_705.0391m);
    }

    [Fact]
    public void An_intraday_chart_that_does_not_reach_24_hours_back_has_no_reference()
    {
        YahooParser.Rolling24h("""
            {"chart":{"result":[{"meta":{"currency":"EUR","regularMarketPrice":100.0,"regularMarketTime":1791192388},
            "timestamp":[1791190000,1791192388],"indicators":{"quote":[{"open":[99.0,100.0],"close":[99.5,100.0]}]}}]}}
            """).ShouldBeNull();
        YahooParser.Rolling24h(Fixture("chart-not-found.json")).ShouldBeNull();
    }

    [Fact]
    public void Coins_change_over_24_hours_when_the_reference_matches_the_current_price()
    {
        var priceAsOf = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
        var referenceAt = new DateTimeOffset(2026, 10, 4, 9, 26, 28, TimeSpan.Zero);

        var reference = DayChange.Reference24h(75_705.0391m, referenceAt, priceAsOf);
        reference.ShouldBe(75_705.0391m);
        // 0.5 BTC × (76 567.30 − 75 705.0391) = 431.13 EUR
        var change = DayChange.Amount(0.5m, 76_567.3m, reference, 1m);
        change.ShouldBe(431.13m);
        DayChange.Percent(change, 0.5m * 76_567.3m).ShouldBe(0.01139m);

        // A reference from an older refresh (the latest one could not fetch it) is not a 24-hour change.
        DayChange.Reference24h(75_705m, referenceAt.AddHours(-6), priceAsOf).ShouldBeNull();
        DayChange.Reference24h(null, null, priceAsOf).ShouldBeNull();
    }

    [Fact]
    public void A_total_of_shares_and_coins_has_a_mixed_basis()
    {
        DayChange.Combine([]).ShouldBe(DayChangeBasis.PreviousClose);
        DayChange.Combine([DayChangeBasis.Rolling24Hours, DayChangeBasis.Rolling24Hours])
            .ShouldBe(DayChangeBasis.Rolling24Hours);
        DayChange.Combine([DayChangeBasis.PreviousClose, DayChangeBasis.Rolling24Hours])
            .ShouldBe(DayChangeBasis.Mixed);
    }

    [Fact]
    public void An_older_close_is_that_days_final_price()
    {
        ManualCrypto.PriceAsOf(new DateOnly(2026, 10, 3), Now)
            .ShouldBe(new DateTimeOffset(2026, 10, 3, 23, 59, 59, TimeSpan.Zero));
    }

    [Fact]
    public void Coin_search_keeps_cryptocurrencies_once_per_coin()
    {
        YahooParser.Coins(Fixture("search-crypto-btc.json")).ShouldBe([new CoinMatch("BTC", "BTC", "Bitcoin")]);

        var pepe = YahooParser.Coins(Fixture("search-crypto-pepe.json"));
        // Yahoo's disambiguated ids stay the identity; the plain ticker is shown.
        pepe[0].ShouldBe(new CoinMatch("PEPE24478", "PEPE", "Pepe"));
        pepe.Select(c => c.Id).ShouldBeUnique();
        YahooParser.Coins("""{"quotes":[{"symbol":"BTC=F","quoteType":"FUTURE"}]}""").ShouldBeEmpty();
    }

    [Fact]
    public void Coins_are_hinted_from_their_manual_id_and_try_the_euro_pair_first()
    {
        var security = Security.Create(null, "KAS", null, "Kaspa", "EUR", AssetClass.Crypto);
        var hints = ListingHints.For(security, [(DataSource.Manual, "KAS")]);

        hints.ShouldBe([("KAS", ListingHints.Crypto)]);
        YahooParser.Candidates(new ListingQuery(null, "KAS", "EUR", hints), []).ShouldBe(["KAS-EUR", "KAS-USD"]);
    }

    [Fact]
    public async Task A_coin_without_a_euro_pair_resolves_to_its_dollar_pair()
    {
        var handler = new RecordedCrypto();
        var source = new YahooPriceHistorySource(new HttpClient(handler), NullLogger<YahooPriceHistorySource>.Instance);

        (await source.ResolveAsync(new ListingQuery(null, "BTC", "EUR", [("BTC", ListingHints.Crypto)]),
            CancellationToken.None)).ShouldBe(new ListingMatch("BTC-EUR", "EUR"));
        (await source.ResolveAsync(new ListingQuery(null, "KAS", "EUR", [("KAS", ListingHints.Crypto)]),
            CancellationToken.None)).ShouldBe(new ListingMatch("KAS-USD", "USD"));
        handler.Paths.ShouldBe(["/v8/finance/chart/BTC-EUR", "/v8/finance/chart/KAS-EUR", "/v8/finance/chart/KAS-USD"]);
    }

    [Fact]
    public async Task Coin_search_sends_only_the_typed_text()
    {
        var handler = new RecordedCrypto();
        var source = new YahooPriceHistorySource(new HttpClient(handler), NullLogger<YahooPriceHistorySource>.Instance);

        var coins = await source.SearchCoinsAsync(" btc ", CancellationToken.None);

        coins.Single().Id.ShouldBe("BTC");
        handler.Paths.ShouldBe(["/v1/finance/search"]);
        handler.Queries[0].ShouldStartWith("?q=btc&");
    }

    [Fact]
    public void Crypto_charts_are_daily_utc_bars_with_todays_live_price_last()
    {
        var btc = YahooParser.Chart(Fixture("chart-BTC-EUR.json"))!;
        btc.Currency.ShouldBe("EUR");
        btc.Closes.Count.ShouldBe(8);
        btc.Closes[0].ShouldBe(new DailyClose(new DateOnly(2026, 9, 24), 74188.9375m));
        btc.Closes[^1].Date.ShouldBe(new DateOnly(2026, 10, 1));

        // Fractions of a cent keep their precision.
        var kas = YahooParser.Chart(Fixture("chart-KAS-USD.json"))!;
        kas.Currency.ShouldBe("USD");
        kas.Closes[0].Close.ShouldBe(0.0398349985m);
    }

    /// <summary>Serves recorded crypto responses; KAS has no EUR pair, like on Yahoo.</summary>
    private sealed class RecordedCrypto : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Queries.Add(request.RequestUri.Query);
            var file = path switch
            {
                "/v1/finance/search" => "search-crypto-btc.json",
                "/v8/finance/chart/BTC-EUR" => "chart-BTC-EUR.json",
                "/v8/finance/chart/KAS-USD" => "chart-KAS-USD.json",
                _ => null,
            };
            return Task.FromResult(new HttpResponseMessage(file is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture(file ?? "chart-not-found.json"), Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
