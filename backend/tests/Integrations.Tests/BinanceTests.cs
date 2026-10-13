using System.Web;
using Integrations.Application.Contracts;
using Integrations.Infrastructure;
using Integrations.Infrastructure.Binance;
using Investments.Application.Sync;
using Investments.Domain;
using SharedKernel.Http;

namespace Integrations.Tests;

public sealed class BinanceTests
{
    private static long Ms(int month, int day) => new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private const string ReadOnlyKey = """
        {"ipRestrict":true,"createTime":1,"enableReading":true,"enableSpotAndMarginTrading":false,
         "enableWithdrawals":false,"enableInternalTransfer":false,"enableMargin":false,"enableFutures":false,
         "permitsUniversalTransfer":false,"enableVanillaOptions":false,"enablePortfolioMarginTrading":false,
         "enableFixApiTrade":false,"enableFixReadOnly":false}
        """;

    private const string Account = """
        {"canTrade":true,"balances":[
          {"asset":"BTC","free":"0.10000000","locked":"0.00000000"},
          {"asset":"LDBTC","free":"0.05000000","locked":"0.00000000"},
          {"asset":"EUR","free":"250.50","locked":"0.00"},
          {"asset":"ETH","free":"1.00000000","locked":"0.00000000"},
          {"asset":"XYZ","free":"5.00000000","locked":"0.00000000"}]}
        """;

    private const string Prices = """
        [{"symbol":"BTCEUR","price":"60000.00"},{"symbol":"BTCUSDT","price":"66000.00"},
         {"symbol":"ETHUSDT","price":"3300.00"},{"symbol":"EURUSDT","price":"1.1000"}]
        """;

    private static string Kline(long openTime, string close) => $"""[{openTime},"1","1","1","{close}","1",{openTime + 86_399_999},"1",1,"1","1","0"]""";

    private static string Empty => """{"rows":[],"total":0}""";

    private static string FiatEmpty => """{"code":"000000","data":[],"total":0}""";

    private static string ConvertEmpty => """{"list":[],"moreData":false}""";

    private static (BinanceProvider Provider, ScriptedHandler Http) Provider(string restrictions = ReadOnlyKey)
    {
        var http = new ScriptedHandler()
            .On("/sapi/v1/account/apiRestrictions", restrictions)
            .On("/api/v3/account", Account)
            .On("/sapi/v1/simple-earn/flexible/position", """{"rows":[{"asset":"BTC","totalAmount":"0.05","latestAnnualPercentageRate":"0.01"}],"total":1}""")
            .On("/sapi/v1/simple-earn/locked/position", """{"rows":[{"asset":"ETH","amount":"0.5","positionId":7}],"total":1}""")
            .On("/api/v3/ticker/price", Prices)
            // BTC: 0.1 bought at 50,000 EUR, half sold (the average stays), then 0.05 bought for 3,300 USDT (3,000 EUR).
            .On("/api/v3/myTrades?symbol=BTCEUR", $$"""
                [{"id":1,"price":"50000","qty":"0.1","quoteQty":"5000","time":{{Ms(1, 10)}},"isBuyer":true},
                 {"id":2,"price":"55000","qty":"0.05","quoteQty":"2750","time":{{Ms(2, 10)}},"isBuyer":false}]
                """)
            .On("/api/v3/myTrades?symbol=BTCUSDT", $$"""
                [{"id":9,"price":"66000","qty":"0.05","quoteQty":"3300","time":{{Ms(9, 21)}},"isBuyer":true}]
                """)
            .On("/api/v3/myTrades?symbol=ETHUSDT", "[]")
            .On("/api/v3/klines?symbol=EURUSDT", $"[{Kline(Ms(9, 21), "1.10")}]")
            .On("/api/v3/klines?symbol=BTCEUR", $"[{Kline(Ms(9, 19), "57000")},{Kline(Ms(9, 20), "58000")}]")
            .On("/api/v3/klines?symbol=ETHUSDT", $"[{Kline(Ms(9, 21), "3300")}]")
            // One reward each, in the first window; every later window is empty.
            .On("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REALTIME",
                $$"""{"rows":[{"asset":"BTC","rewards":"0.0001","projectId":"BTC001","type":"REALTIME","time":{{Ms(9, 20) + 3_600_000}}}],"total":1}""")
            .On("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REALTIME", Empty)
            .On("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=BONUS", Empty)
            .On("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REWARDS", Empty)
            .On("/sapi/v1/simple-earn/locked/history/rewardsRecord",
                $$"""{"rows":[{"positionId":7,"time":{{Ms(9, 21)}},"asset":"ETH","lockPeriod":"30","amount":"0.01","type":"Locked Rewards"}],"total":1}""")
            .On("/sapi/v1/simple-earn/locked/history/rewardsRecord", Empty)
            // Fiat in and out: one SEPA deposit, one card purchase (and one that failed), one withdrawal.
            .On("/sapi/v1/fiat/orders?transactionType=0", $$"""
                {"code":"000000","message":"success","data":[{"orderNo":"D1","fiatCurrency":"EUR","indicatedAmount":"500.00",
                 "amount":"500.00","totalFee":"0.00","method":"SEPA","status":"Successful","createTime":{{Ms(9, 1)}}}],"total":1,"success":true}
                """)
            .On("/sapi/v1/fiat/orders?transactionType=0", FiatEmpty)
            .On("/sapi/v1/fiat/orders?transactionType=1", $$"""
                {"code":"000000","data":[{"orderNo":"W1","fiatCurrency":"EUR","indicatedAmount":"50.00","amount":"49.00",
                 "method":"SEPA","status":"Successful","createTime":{{Ms(9, 5)}}}],"total":1}
                """)
            .On("/sapi/v1/fiat/orders?transactionType=1", FiatEmpty)
            .On("/sapi/v1/fiat/payments", $$"""
                {"code":"000000","data":[
                 {"orderNo":"P1","sourceAmount":"100.00","totalFee":"2.00","fiatCurrency":"EUR","obtainAmount":"0.0016","cryptoCurrency":"BTC","status":"Completed","createTime":{{Ms(9, 10)}}},
                 {"orderNo":"P2","sourceAmount":"80.00","fiatCurrency":"EUR","obtainAmount":"0.0012","cryptoCurrency":"BTC","status":"Failed","createTime":{{Ms(9, 11)}}}],"total":2}
                """)
            .On("/sapi/v1/fiat/payments", FiatEmpty)
            .On("/sapi/v1/convert/tradeFlow", ConvertEmpty)
            .On("/sapi/v1/capital/deposit/hisrec", "[]")
            .On("/sapi/v1/capital/withdraw/history", "[]");
        var guard = new AllowListHttpHandler(BinanceClient.AllowList()) { InnerHandler = http };
        var clock = new InstantTimeProvider();
        var client = new BinanceClient(new HttpClient(guard), new RateGate(clock), clock).Configure("key", "secret");
        return (new BinanceProvider(client, clock), http);
    }

    [Fact]
    public async Task One_position_per_coin_with_spot_and_earn_valued_in_euros()
    {
        var (provider, http) = Provider();

        var snapshot = await provider.GetAccountSnapshotAsync(CancellationToken.None);
        var positions = await provider.GetPositionsAsync(CancellationToken.None);

        snapshot.Currency.ShouldBe("EUR");
        snapshot.Cash.ShouldBe(250.50m);
        snapshot.TotalValue.ShouldBe(250.50m + 0.15m * 60_000m + 1.5m * 3_000m);
        positions.Select(p => p.Security.Symbol).ShouldBe(["BTC", "ETH"]); // XYZ has no EUR or dollar price
        var btc = positions[0];
        btc.Quantity.ShouldBe(0.15m); // spot 0.1 + flexible 0.05; the LDBTC shadow row is not counted again
        btc.LastPrice.ShouldBe(60_000m);
        // As Binance shows it, the average of everything that came in (the sale does not change it): 0.1 bought for
        // 5,000 EUR, 0.0016 by card for 100 EUR of which 2 EUR card fee (not part of the cost), an Earn reward of
        // 0.0001 worth 5.80 EUR that day, then 0.05 for 3,300 USDT (3,000 EUR).
        btc.AveragePrice.ShouldBe(decimal.Round((5_000m + 98m + 5.8m + 3_000m) / 0.1517m, 8));
        btc.CostKnown.ShouldBeTrue();
        btc.Security.AssetClass.ShouldBe(AssetClass.Crypto);
        btc.Security.Currency.ShouldBe("EUR");
        btc.Security.Isin.ShouldBeNull();
        var eth = positions[1];
        eth.Quantity.ShouldBe(1.5m); // spot 1 + locked 0.5
        eth.LastPrice.ShouldBe(3_000m); // 3,300 USDT / 1.10
        eth.AveragePrice.ShouldBe(3_000m); // no fills: only the locked reward, worth 30 EUR for 0.01

        http.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
        var signed = http.Requests.Where(r => r.RequestUri!.AbsolutePath != "/api/v3/ticker/price" &&
                                              r.RequestUri.AbsolutePath != "/api/v3/klines").ToList();
        signed.ShouldAllBe(r => r.Headers.GetValues("X-MBX-APIKEY").Single() == "key");
        signed.ShouldAllBe(r => HttpUtility.ParseQueryString(r.RequestUri!.Query)["signature"]!.Length == 64);
    }

    [Fact]
    public async Task Earn_rewards_are_income_valued_at_the_days_close()
    {
        var (provider, _) = Provider();

        var rewards = await provider.GetDividendsAsync(null, CancellationToken.None);

        rewards.Count.ShouldBe(2);
        var btc = rewards.Single(r => r.Security.Symbol == "BTC");
        btc.PaidOn.ShouldBe(new DateOnly(2026, 9, 20));
        btc.NetAmount.ShouldBe(5.8m); // 0.0001 × 58,000
        btc.Currency.ShouldBe("EUR");
        btc.Quantity.ShouldBe(0.0001m);
        btc.ExternalId.ShouldStartWith("binance:earn:flexible-realtime:BTC:");
        var eth = rewards.Single(r => r.Security.Symbol == "ETH");
        eth.NetAmount.ShouldBe(30m); // 0.01 × (3,300 / 1.10)
        eth.ExternalId.ShouldStartWith("binance:earn:locked:ETH:");

        // Same input, same ids: a re-sync never books a reward twice.
        var (again, _) = Provider();
        (await again.GetDividendsAsync(null, CancellationToken.None)).Select(r => r.ExternalId)
            .ShouldBe(rewards.Select(r => r.ExternalId), ignoreOrder: true);
    }

    [Fact]
    public async Task Fiat_deposits_card_purchases_and_withdrawals_are_the_accounts_flows()
    {
        var (provider, _) = Provider();

        var flows = (await provider.GetTransactionsAsync(null, CancellationToken.None)).Select(t => t.Cash!).ToList();

        flows.Select(f => (f.ExternalId, f.Type, f.Amount)).ShouldBe(
        [
            ("binance:fiat:deposit:D1", CashMovementType.Deposit, 500m),
            ("binance:fiat:withdrawal:W1", CashMovementType.Withdrawal, -50m), // what was sent out, before fees
            ("binance:fiat:purchase:P1", CashMovementType.Deposit, 100m),       // the failed card payment is not money in
        ], ignoreOrder: true);
        flows.ShouldAllBe(f => f.Currency == "EUR");
    }

    [Theory]
    [InlineData("enableSpotAndMarginTrading", "spot & margin trading")]
    [InlineData("enableWithdrawals", "withdrawals")]
    [InlineData("enableInternalTransfer", "transfers")]
    [InlineData("enableFutures", "futures")]
    public async Task A_key_that_can_do_more_than_read_is_refused_before_reading(string permission, string named)
    {
        var (provider, http) = Provider(ReadOnlyKey.Replace($"\"{permission}\":false", $"\"{permission}\":true"));

        var error = await Should.ThrowAsync<ProviderConfigurationException>(
            () => provider.GetPositionsAsync(CancellationToken.None));

        error.Message.ShouldContain(named);
        http.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/sapi/v1/account/apiRestrictions");
    }

    [Theory]
    [InlineData("POST", "/api/v3/order")]
    [InlineData("GET", "/sapi/v1/capital/withdraw/apply")]
    [InlineData("POST", "/sapi/v1/simple-earn/flexible/redeem")]
    [InlineData("POST", "/sapi/v1/asset/transfer")]
    [InlineData("POST", "/sapi/v1/convert/acceptQuote")]
    public async Task Only_allow_listed_read_requests_can_leave(string method, string path)
    {
        var guard = new AllowListHttpHandler(BinanceClient.AllowList()) { InnerHandler = new ScriptedHandler() };
        using var http = new HttpClient(guard);

        await Should.ThrowAsync<BlockedRequestException>(() =>
            http.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"https://{BinanceClient.Host}{path}")));
    }

    /// <summary>SOL: converted from BTC, partly converted away, one received and some sent; ADA: no history.</summary>
    private static ScriptedHandler SolHistory(ScriptedHandler http)
    {
        return http.Replace("/api/v3/account", """
                {"balances":[{"asset":"SOL","free":"2","locked":"0"},{"asset":"ADA","free":"100","locked":"0"}]}
                """)
            .Replace("/sapi/v1/simple-earn/flexible/position", Empty)
            .Replace("/sapi/v1/simple-earn/locked/position", Empty)
            .Replace("/api/v3/ticker/price", """
                [{"symbol":"SOLEUR","price":"150"},{"symbol":"ADAEUR","price":"0.5"},{"symbol":"BTCEUR","price":"60000"},
                 {"symbol":"EURUSDT","price":"1.10"}]
                """)
            .Replace("/api/v3/klines?symbol=BTCEUR", $"[{Kline(Ms(3, 1), "50000")}]")
            .Replace("/api/v3/klines?symbol=SOLEUR", $"[{Kline(Ms(5, 1), "120")}]")
            .Replace("/api/v3/myTrades?symbol=SOLEUR", "[]")
            .Replace("/api/v3/myTrades?symbol=ADAEUR", "[]")
            .Replace("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REALTIME", Empty)
            .Replace("/sapi/v1/simple-earn/locked/history/rewardsRecord", Empty)
            .Replace("/sapi/v1/fiat/payments", FiatEmpty)
            // 0.01 BTC (500 EUR that day) converted to 5 SOL, then 1 SOL converted to dollars; a failed order is ignored.
            .Replace("/sapi/v1/convert/tradeFlow", $$"""
                {"list":[
                 {"orderStatus":"SUCCESS","fromAsset":"BTC","fromAmount":"0.01","toAsset":"SOL","toAmount":"5","createTime":{{Ms(3, 1)}}},
                 {"orderStatus":"SUCCESS","fromAsset":"SOL","fromAmount":"1","toAsset":"USDT","toAmount":"160","createTime":{{Ms(4, 1)}}},
                 {"orderStatus":"FAIL","fromAsset":"EUR","fromAmount":"1","toAsset":"SOL","toAmount":"9","createTime":{{Ms(4, 2)}}}],
                 "moreData":false}
                """)
            .On("/sapi/v1/convert/tradeFlow", ConvertEmpty)
            // 1 SOL received from another wallet (120 EUR that day), 2.9 SOL sent out plus a 0.1 fee.
            .Replace("/sapi/v1/capital/deposit/hisrec", $$"""
                [{"coin":"SOL","amount":"1","status":1,"insertTime":{{Ms(5, 1)}}}]
                """)
            .On("/sapi/v1/capital/deposit/hisrec", "[]")
            .Replace("/sapi/v1/capital/withdraw/history", """
                [{"coin":"SOL","amount":"2.9","transactionFee":"0.1","status":6,"applyTime":"2026-06-01 10:00:00"},
                 {"coin":"SOL","amount":"50","transactionFee":"0.1","status":3,"applyTime":"2026-06-02 10:00:00"}]
                """)
            .On("/sapi/v1/capital/withdraw/history", "[]");

    }

    [Fact]
    public async Task Converted_received_and_sent_coins_count_towards_the_cost()
    {
        var (provider, http) = Provider();
        SolHistory(http);

        var positions = await provider.GetPositionsAsync(CancellationToken.None);

        var sol = positions.Single(p => p.Security.Symbol == "SOL");
        sol.AveragePrice.ShouldBe(103.33333333m); // 5 in for 500 and 1 received worth 120: 620 / 6
        sol.CostKnown.ShouldBeTrue();
        var ada = positions.Single(p => p.Security.Symbol == "ADA");
        ada.CostKnown.ShouldBeFalse(); // no history at all: the first synced price is kept as its cost
        ada.AveragePrice.ShouldBe(0.5m);
        // The whole history from the account's start, in windows Binance accepts (less than 90 days each).
        var sent = http.Requests.Where(r => r.RequestUri!.AbsolutePath == "/sapi/v1/capital/withdraw/history")
            .Select(r => HttpUtility.ParseQueryString(r.RequestUri!.Query))
            .Select(q => (From: long.Parse(q["startTime"]!), To: long.Parse(q["endTime"]!))).ToList();
        sent[0].From.ShouldBe(BinanceProvider.HistoryStart.ToUnixTimeMilliseconds());
        sent.ShouldAllBe(w => w.To - w.From <= TimeSpan.FromDays(BinanceProvider.CoinWindowDays).TotalMilliseconds);
        sent.Zip(sent.Skip(1)).ShouldAllBe(p => p.First.To == p.Second.From);
    }

    [Fact]
    public async Task The_history_is_read_once_and_kept_between_syncs()
    {
        var (first, http) = Provider();
        SolHistory(http);
        await first.GetPositionsAsync(CancellationToken.None);
        var kept = first.HistoryState;
        kept.ShouldNotBeNull();

        // Next sync: Binance would no longer return those movements (the scripted ones were consumed), yet the cost
        // is the same, and only the days since the last sync are read.
        var (second, again) = Provider();
        second.HistoryState = kept;
        var positions = await second.GetPositionsAsync(CancellationToken.None);

        positions.ShouldContain(p => p.Security.Symbol == "BTC");
        var windows = again.Requests.Where(r => r.RequestUri!.AbsolutePath == "/sapi/v1/capital/withdraw/history")
            .ToList();
        windows.Count.ShouldBe(1);
        long.Parse(HttpUtility.ParseQueryString(windows[0].RequestUri!.Query)["startTime"]!)
            .ShouldBeGreaterThan(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());

        var (third, http3) = Provider();
        SolHistory(http3).Replace("/sapi/v1/convert/tradeFlow", ConvertEmpty)
            .Replace("/sapi/v1/capital/deposit/hisrec", "[]")
            .Replace("/sapi/v1/capital/withdraw/history", "[]");
        third.HistoryState = kept;
        (await third.GetPositionsAsync(CancellationToken.None)).Single(p => p.Security.Symbol == "SOL")
            .AveragePrice.ShouldBe(103.33333333m);
    }

    [Fact]
    public async Task Old_purchases_count_even_after_coins_were_sent_away()
    {
        // 0.1 BTC bought by card in 2019 for 3,000 EUR, 0.08 sent to a cold wallet in 2021, 0.01 bought by card in
        // 2026 for 600 EUR: like Binance, the average of every purchase, (3,000 + 600) / 0.11.
        var (provider, http) = Provider();
        static long At(int y, int m) => new DateTimeOffset(y, m, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        http.Replace("/api/v3/account", """{"balances":[{"asset":"BTC","free":"0.03","locked":"0"}]}""")
            .Replace("/sapi/v1/simple-earn/flexible/position", Empty)
            .Replace("/sapi/v1/simple-earn/locked/position", Empty)
            .Replace("/api/v3/myTrades?symbol=BTCEUR", "[]")
            .Replace("/api/v3/myTrades?symbol=BTCUSDT", "[]")
            .Replace("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REALTIME", Empty)
            .Replace("/sapi/v1/simple-earn/locked/history/rewardsRecord", Empty)
            .Replace("/sapi/v1/fiat/payments", $$"""
                {"code":"000000","data":[
                 {"orderNo":"O1","sourceAmount":"3000","fiatCurrency":"EUR","obtainAmount":"0.1","cryptoCurrency":"BTC","status":"Completed","createTime":{{At(2019, 3)}}},
                 {"orderNo":"O2","sourceAmount":"600","fiatCurrency":"EUR","obtainAmount":"0.01","cryptoCurrency":"BTC","status":"Completed","createTime":{{At(2026, 3)}}}],
                 "total":2}
                """)
            .Replace("/sapi/v1/capital/withdraw/history", """
                [{"coin":"BTC","amount":"0.0799","transactionFee":"0.0001","status":6,"applyTime":"2021-05-01 10:00:00"}]
                """)
            .On("/sapi/v1/capital/withdraw/history", "[]");

        var btc = (await provider.GetPositionsAsync(CancellationToken.None)).Single();

        btc.AveragePrice.ShouldBe(32_727.27272727m); // (3,000 + 600) / 0.11, like Binance's cost price
    }

    [Fact]
    public async Task A_purchase_costs_the_market_price_that_day_and_the_amount_paid_is_shown_beside_it()
    {
        // 0.001 BTC bought with the Buy button on 20 September for 70 EUR (1.50 fee + spread); BTC closed at 58,000.
        var (provider, http) = Provider();
        http.Replace("/api/v3/account", """{"balances":[{"asset":"BTC","free":"0.001","locked":"0"}]}""")
            .Replace("/sapi/v1/simple-earn/flexible/position", Empty)
            .Replace("/sapi/v1/simple-earn/locked/position", Empty)
            .Replace("/api/v3/myTrades?symbol=BTCEUR", "[]")
            .Replace("/api/v3/myTrades?symbol=BTCUSDT", "[]")
            .Replace("/sapi/v1/simple-earn/flexible/history/rewardsRecord?type=REALTIME", Empty)
            .Replace("/sapi/v1/simple-earn/locked/history/rewardsRecord", Empty)
            .Replace("/sapi/v1/fiat/payments", $$"""
                {"code":"000000","data":[
                 {"orderNo":"B1","sourceAmount":"70","totalFee":"1.50","fiatCurrency":"EUR","obtainAmount":"0.001","cryptoCurrency":"BTC","status":"Completed","paymentMethod":"Cash Balance","createTime":{{Ms(9, 20) + 3_600_000}}}],
                 "total":1}
                """);

        var btc = (await provider.GetCostBreakdownAsync(CancellationToken.None)).Single();

        btc.AverageCost.ShouldBe(58_000m); // the market price, as Binance's average cost
        var card = btc.Sources.Single(l => l.Kind == "card");
        card.EurValue.ShouldBe(58m);
        card.Paid.ShouldBe(70m);
    }

    [Fact]
    public async Task The_cost_breakdown_shows_each_kind_of_movement()
    {
        var (provider, http) = Provider();
        SolHistory(http);

        var breakdown = await provider.GetCostBreakdownAsync(CancellationToken.None);

        var sol = breakdown.Single(b => b.Asset == "SOL");
        sol.Held.ShouldBe(2m);
        sol.Tracked.ShouldBe(2m);
        sol.AverageCost.ShouldBe(103.33333333m);
        sol.Sources.Select(l => (l.Kind, l.Count, l.Quantity, l.EurValue)).ShouldBe(
        [
            ("convert", 1, 5m, 500m),
            ("convert", 1, -1m, 0m),
            ("received", 1, 1m, 120m),
            ("sent", 1, -3m, 0m),
        ], ignoreOrder: true);
        var ada = breakdown.Single(b => b.Asset == "ADA");
        ada.AverageCost.ShouldBeNull();
        ada.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void The_cost_is_kept_in_dollars_and_shown_at_todays_rate_as_binance_does()
    {
        // 1 coin worth 100 EUR when 1 EUR bought 1.00 dollar: 100 dollars; today 1 EUR buys 1.25 dollars → 80 EUR.
        var day = new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var closes = new Dictionary<DateOnly, decimal> { [DateOnly.FromDateTime(day.UtcDateTime)] = 1.00m };

        BinanceValuation.AverageCostInDollars([CostEvent.In(day.ToUnixTimeMilliseconds(), 1, 100)], closes, 1.25m)
            .ShouldBe(80m);
        BinanceValuation.AverageCostInDollars([CostEvent.In(day.ToUnixTimeMilliseconds(), 1, 100)], closes, 0m)
            .ShouldBe(100m); // no dollar rate: the EUR average
    }

    [Fact]
    public void The_average_is_of_everything_that_came_in_and_unknown_without_coins_in()
    {
        BinanceValuation.AverageCost([CostEvent.In(1, 2, 200), CostEvent.Out(2, 1), CostEvent.In(3, 1, 300)])
            .ShouldBe(166.66666667m); // (200 + 300) / 3: what went out does not change it
        BinanceValuation.AverageCost([CostEvent.In(1, 1, 100), CostEvent.Out(2, 1)]).ShouldBe(100m);
        BinanceValuation.AverageCost([CostEvent.Out(1, 1)]).ShouldBeNull();
    }

    [Fact]
    public void Average_cost_is_unknown_without_buys()
    {
        BinanceValuation.AverageCost([], new Dictionary<DateOnly, decimal>(), 1.1m).ShouldBeNull();
    }
}
