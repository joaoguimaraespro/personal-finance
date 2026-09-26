using System.Net;
using Integrations.Application.Contracts;
using Integrations.Infrastructure;
using Integrations.Infrastructure.Trading212;
using Investments.Domain;
using SharedKernel.Http;

namespace Integrations.Tests;

public sealed class Trading212Tests
{
    private const string Api = "/api/v0/equity";

    // Payloads follow the schemas of the official Public API v0 reference.
    private const string Summary = """
        {"id":123,"currency":"EUR","totalValue":10500.5,
         "cash":{"availableToTrade":480.25,"inPies":0,"reservedForOrders":20},
         "investments":{"currentValue":10000.25,"totalCost":9000,"realizedProfitLoss":150.4,"unrealizedProfitLoss":1000.25}}
        """;

    private const string Positions = """
        [{"quantity":12.5,"quantityAvailableForTrading":12.5,"quantityInPies":0,"averagePricePaid":100.1,"currentPrice":120.2,
          "createdAt":"2025-01-02T10:00:00Z","instrument":{"ticker":"VWCEd_EQ","isin":"IE00BK5BQT80","name":"Vanguard FTSE All-World (Acc)","currency":"EUR"},
          "walletImpact":{"currency":"EUR","currentValue":1502.5,"totalCost":1251.25,"unrealizedProfitLoss":251.25,"fxImpact":0}},
         {"quantity":3,"quantityAvailableForTrading":3,"quantityInPies":0,"averagePricePaid":180,"currentPrice":200,
          "createdAt":"2025-02-02T10:00:00Z","instrument":{"ticker":"AAPL_US_EQ","isin":"US0378331005","name":"Apple","currency":"USD"},
          "walletImpact":{"currency":"EUR","currentValue":545.4,"totalCost":490.9,"unrealizedProfitLoss":54.5,"fxImpact":-3.1}}]
        """;

    private const string Instruments = """
        [{"ticker":"VWCEd_EQ","isin":"IE00BK5BQT80","currencyCode":"EUR","name":"Vanguard FTSE All-World (Acc)","shortName":"VWCE","type":"ETF"},
         {"ticker":"AAPL_US_EQ","isin":"US0378331005","currencyCode":"USD","name":"Apple","shortName":"AAPL","type":"STOCK"}]
        """;

    private const string OrdersPage1 = """
        {"items":[
          {"order":{"id":2,"side":"SELL","currency":"USD","ticker":"AAPL_US_EQ","status":"FILLED",
                    "instrument":{"ticker":"AAPL_US_EQ","isin":"US0378331005","name":"Apple","currency":"USD"}},
           "fill":{"id":22,"price":210,"quantity":1,"filledAt":"2026-09-20T14:30:05.123Z","type":"TRADE","tradingMethod":"TOTV",
                   "walletImpact":{"currency":"EUR","fxRate":0.9,"netValue":188.5,"realisedProfitLoss":27.5,
                                   "taxes":[{"name":"CURRENCY_CONVERSION_FEE","quantity":0.28,"currency":"EUR","chargedAt":"2026-09-20T14:30:05Z"}]}}},
          {"order":{"id":3,"side":"BUY","currency":"EUR","ticker":"VWCEd_EQ","status":"CANCELLED"},"fill":null}
        ],"nextPagePath":"/api/v0/equity/history/orders?cursor=abc&limit=50"}
        """;

    private const string OrdersPage2 = """
        {"items":[
          {"order":{"id":1,"side":"BUY","currency":"EUR","ticker":"VWCEd_EQ","status":"FILLED",
                    "instrument":{"ticker":"VWCEd_EQ","isin":"IE00BK5BQT80","name":"Vanguard FTSE All-World (Acc)","currency":"EUR"}},
           "fill":{"id":11,"price":100.1,"quantity":12.5,"filledAt":"2025-01-02T10:00:00Z","type":"TRADE",
                   "walletImpact":{"currency":"EUR","fxRate":1,"netValue":1251.25,"realisedProfitLoss":0,"taxes":[]}}}
        ],"nextPagePath":null}
        """;

    private const string Dividends = """
        {"items":[{"amount":0.62,"amountInEuro":0.62,"currency":"EUR","grossAmountPerShare":0.25,"paidOn":"2026-08-15T00:00:00Z",
                   "quantity":3,"reference":"div-1","ticker":"AAPL_US_EQ","tickerCurrency":"USD","type":"ORDINARY",
                   "instrument":{"ticker":"AAPL_US_EQ","isin":"US0378331005","name":"Apple","currency":"USD"}}],
         "nextPagePath":null}
        """;

    private const string Transactions = """
        {"items":[{"amount":1000,"currency":"EUR","dateTime":"2025-01-01T09:00:00Z","reference":"t1","type":"DEPOSIT"},
                  {"amount":-50,"currency":"EUR","dateTime":"2026-03-01T09:00:00Z","reference":"t2","type":"WITHDRAW"},
                  {"amount":1.23,"currency":"EUR","dateTime":"2026-09-01T00:00:00Z","reference":"t3","type":"INTEREST_ON_FREE_CASH"}],
         "nextPagePath":null}
        """;

    private static (Trading212Provider Provider, ScriptedHandler Http) Provider(Action<ScriptedHandler>? script = null)
    {
        var http = new ScriptedHandler()
            .On($"{Api}/account/summary", Summary)
            .On($"{Api}/positions", Positions)
            .On($"{Api}/metadata/instruments", Instruments)
            .On($"{Api}/history/orders?limit=50", OrdersPage1)
            .On($"{Api}/history/orders?cursor=abc", OrdersPage2)
            .On($"{Api}/history/dividends", Dividends)
            .On($"{Api}/history/transactions", Transactions);
        script?.Invoke(http);
        var guard = new AllowListHttpHandler(Trading212Client.AllowList(Trading212Client.LiveHost)) { InnerHandler = http };
        var client = new Trading212Client(new HttpClient(guard), new RateGate(new InstantTimeProvider()), new InstantTimeProvider())
            .Configure("key", "secret", demo: false);
        return (new Trading212Provider(client), http);
    }

    [Fact]
    public async Task Maps_account_positions_and_instrument_types()
    {
        var (provider, http) = Provider();

        var snapshot = await provider.GetAccountSnapshotAsync(CancellationToken.None);
        var positions = await provider.GetPositionsAsync(CancellationToken.None);

        snapshot.Currency.ShouldBe("EUR");
        snapshot.Cash.ShouldBe(500.25m);
        var vwce = positions.Single(p => p.Security.Isin == "IE00BK5BQT80");
        vwce.Security.Symbol.ShouldBe("VWCE");
        vwce.Security.AssetClass.ShouldBe(AssetClass.Etf);
        vwce.Quantity.ShouldBe(12.5m);
        vwce.AveragePrice.ShouldBe(100.1m);
        positions.Single(p => p.Security.Symbol == "AAPL").Security.AssetClass.ShouldBe(AssetClass.Stock);
        http.Requests.ShouldAllBe(r => r.Headers.Authorization!.Scheme == "Basic");
        http.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Follows_pagination_and_keeps_only_executed_trades()
    {
        var (provider, _) = Provider();

        var tx = await provider.GetTransactionsAsync(null, CancellationToken.None);
        var trades = tx.Where(t => t.Trade is not null).Select(t => t.Trade!).ToList();
        var cash = tx.Where(t => t.Cash is not null).Select(t => t.Cash!).ToList();

        trades.Count.ShouldBe(2); // the cancelled order without a fill is not a trade
        var sell = trades.Single(t => t.Side == TradeSide.Sell);
        sell.NetAmountInAccountCurrency.ShouldBe(188.5m);
        sell.Fees.ShouldBe(0.28m);
        sell.RealizedPnlInAccountCurrency.ShouldBe(27.5m);
        sell.ExternalId.ShouldBe("t212:trade:US0378331005:20260920143005:1");
        trades.Single(t => t.Side == TradeSide.Buy).NetAmountInAccountCurrency.ShouldBe(-1251.25m);

        cash.Single(c => c.Type == CashMovementType.Deposit).Amount.ShouldBe(1000m);
        cash.Single(c => c.Type == CashMovementType.Withdrawal).Amount.ShouldBe(-50m);
        cash.Single(c => c.Type == CashMovementType.Interest).Amount.ShouldBe(1.23m);
    }

    [Fact]
    public async Task Dividend_withholding_is_not_inferred_across_currencies()
    {
        var (provider, _) = Provider();

        var dividend = (await provider.GetDividendsAsync(null, CancellationToken.None)).Single();

        dividend.NetAmount.ShouldBe(0.62m);
        dividend.GrossPerShare.ShouldBeNull(); // USD per share vs EUR net: not comparable
        dividend.ExternalId.ShouldBe("t212:div:US0378331005:20260815:0.62");
    }

    [Fact]
    public async Task Rejected_key_needs_user_attention()
    {
        var (provider, _) = Provider(h => h.Replace($"{Api}/account/summary", "{}", HttpStatusCode.Unauthorized));

        await Should.ThrowAsync<ProviderConfigurationException>(() => provider.GetAccountSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rate_limit_is_honoured_and_retried()
    {
        var reset = new Dictionary<string, string> { ["x-ratelimit-reset"] = "1790000000" };
        var http = new ScriptedHandler()
            .On($"{Api}/account/summary", "{}", HttpStatusCode.TooManyRequests, reset)
            .On($"{Api}/account/summary", Summary);
        var guard = new AllowListHttpHandler(Trading212Client.AllowList(Trading212Client.LiveHost)) { InnerHandler = http };
        var clock = new InstantTimeProvider();
        var client = new Trading212Client(new HttpClient(guard), new RateGate(clock), clock).Configure("k", "s", false);

        var summary = await client.SummaryAsync(CancellationToken.None);

        summary.TotalValue.ShouldBe(10500.5m);
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public void Order_endpoints_are_not_reachable_through_the_client_allow_list()
    {
        var allowed = Trading212Client.AllowList(Trading212Client.LiveHost).ToList();

        foreach (var path in new[] { "/api/v0/equity/orders/market", "/api/v0/equity/orders/limit", "/api/v0/equity/orders",
                     "/api/v0/equity/pies", "/api/v0/equity/orders/1" })
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Delete })
            {
                allowed.ShouldNotContain(a => a.Matches(method, new Uri($"https://live.trading212.com{path}")),
                    $"{method} {path}");
            }
        }
    }

    [Fact]
    public void Csv_rows_get_the_same_keys_as_the_api_and_add_withholding_tax()
    {
        const string csv = """
            Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,Currency (Price / share),Exchange rate,Result,Currency (Result),Total,Currency (Total),Withholding tax,Currency (Withholding tax),Currency conversion fee,Currency (Currency conversion fee),ID
            Deposit,2025-01-01 09:00:00,,,,,,,,,,1000.00,EUR,,,,,
            Market buy,2025-01-02 10:00:00,IE00BK5BQT80,VWCE,"Vanguard FTSE All-World (Acc)",12.5,100.10,EUR,1.00,,EUR,1251.25,EUR,,,,,EOF1
            Market sell,2026-09-20 14:30:05.123,US0378331005,AAPL,Apple,1,210.00,USD,1.11,27.50,EUR,188.50,EUR,,,0.28,EUR,EOF2
            Dividend (Ordinary),2026-08-15 00:00:00,US0378331005,AAPL,Apple,3,0.25,USD,1.11,,,0.62,EUR,0.11,USD,,,
            Currency conversion,2026-09-20 14:30:05,,,,,,,,,,,,,,,,
            Withdrawal,2026-03-01 09:00:00,,,,,,,,,,-50.00,EUR,,,,,
            """;

        var result = Trading212CsvParser.Parse(new StringReader(csv), "EUR");

        result.Warnings.ShouldBeEmpty();
        result.Trades.Select(t => t.ExternalId).ShouldBe(
            ["t212:trade:IE00BK5BQT80:20250102100000:12.5", "t212:trade:US0378331005:20260920143005:1"]);
        result.Cash.Select(c => c.ExternalId).ShouldBe(
            ["t212:cash:Deposit:20250101090000:1000.00", "t212:cash:Withdrawal:20260301090000:50.00"]);
        var dividend = result.Dividends.Single();
        dividend.ExternalId.ShouldBe("t212:div:US0378331005:20260815:0.62");
        // 0.11 USD withheld, converted at the row's own rate: 0.62 EUR / (3 × 0.25 − 0.11) USD
        dividend.WithholdingTax.ShouldBe(0.1066m);
        dividend.GrossAmount.ShouldBe(0.7266m);
    }

    [Fact]
    public void Files_that_are_not_trading212_exports_are_refused() =>
        Should.Throw<InvalidDataException>(() => Trading212CsvParser.Parse(new StringReader("a,b\n1,2"), "EUR"));
}
