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
                 {"orderNo":"P1","sourceAmount":"100.00","fiatCurrency":"EUR","obtainAmount":"0.0016","cryptoCurrency":"BTC","status":"Completed","createTime":{{Ms(9, 10)}}},
                 {"orderNo":"P2","sourceAmount":"80.00","fiatCurrency":"EUR","obtainAmount":"0.0012","cryptoCurrency":"BTC","status":"Failed","createTime":{{Ms(9, 11)}}}],"total":2}
                """)
            .On("/sapi/v1/fiat/payments", FiatEmpty);
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
        btc.AveragePrice.ShouldBe(55_000m); // (2,500 left of the EUR buy + 3,000) / 0.1
        btc.Security.AssetClass.ShouldBe(AssetClass.Crypto);
        btc.Security.Currency.ShouldBe("EUR");
        btc.Security.Isin.ShouldBeNull();
        var eth = positions[1];
        eth.Quantity.ShouldBe(1.5m); // spot 1 + locked 0.5
        eth.LastPrice.ShouldBe(3_000m); // 3,300 USDT / 1.10
        eth.AveragePrice.ShouldBe(3_000m); // no fills: valued from now on, without a gain

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

    [Fact]
    public void Average_cost_is_unknown_without_buys()
    {
        BinanceValuation.AverageCost([], new Dictionary<DateOnly, decimal>(), 1.1m).ShouldBeNull();
    }
}
