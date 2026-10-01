using System.Net;
using System.Text;
using Investments.Application.Abstractions;
using Investments.Application.Prices;
using Investments.Domain;
using Investments.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using SharedKernel.Http;

namespace Investments.Tests;

/// <summary>Symbol resolution and parsing against responses recorded from Yahoo Finance (no live network).</summary>
public sealed class PriceHistoryTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Yahoo", name));

    [Theory]
    [InlineData("VWCEd_EQ", "VWCE", "XETR")]
    [InlineData("VUSAl_EQ", "VUSA", "XLON")]
    [InlineData("IWDAa_EQ", "IWDA", "XAMS")]
    [InlineData("AAPL_US_EQ", "AAPL", "US")]
    public void Trading212_tickers_hint_the_listing_exchange(string ticker, string root, string exchange) =>
        ListingHints.FromTrading212(ticker).ShouldBe((root, exchange));

    [Theory]
    [InlineData("DEMO_VWCE")]
    [InlineData("VWCEx_EQ")]
    [InlineData("SAP_DE_EQ")]
    public void Unknown_ticker_shapes_give_no_hint(string ticker) => ListingHints.FromTrading212(ticker).ShouldBeNull();

    [Fact]
    public void Ibkr_listing_exchanges_are_hints_too()
    {
        var security = Security.Create("IE00BK5BQT80", "VWCE", "IBIS2", "Vanguard FTSE All-World", "EUR", AssetClass.Etf);

        ListingHints.For(security, [(DataSource.InteractiveBrokers, "12345")]).ShouldBe([("VWCE", "XETR")]);
    }

    [Fact]
    public void Search_results_are_read_in_relevance_order()
    {
        YahooParser.SearchSymbols(Fixture("search-IE00BK5BQT80.json")).ShouldBe(["VWRA.L", "IE00BK5BQT80.SG"]);
        YahooParser.SearchSymbols(Fixture("search-US0378331005.json")).ShouldBe(["AAPL"]);
        YahooParser.SearchSymbols("""{"quotes":[{"symbol":"X","quoteType":"OPTION"}]}""").ShouldBeEmpty();
    }

    [Fact]
    public void Candidates_prefer_the_brokers_exchange()
    {
        var query = new ListingQuery("IE00BK5BQT80", "VWCE", "EUR", [("VWCE", "XETR")]);

        YahooParser.Candidates(query, ["VWRA.L", "VWCE.DE", "IE00BK5BQT80.SG"])
            .ShouldBe(["VWCE.DE", "VWRA.L", "IE00BK5BQT80.SG"]);
    }

    [Fact]
    public void Chart_closes_use_the_exchanges_local_dates()
    {
        var series = YahooParser.Chart(Fixture("chart-VWCE.DE.json"))!;

        series.Currency.ShouldBe("EUR");
        series.Closes.Count.ShouldBe(8);
        // 1756710000 = 2025-09-01 07:00 UTC (09:00 in Frankfurt).
        series.Closes[0].ShouldBe(new DailyClose(new DateOnly(2025, 9, 1), 135.88m));
        series.Closes[^1].ShouldBe(new DailyClose(new DateOnly(2025, 9, 10), 137.38m));
    }

    [Fact]
    public void Chart_without_history_or_unknown_symbol_is_empty()
    {
        YahooParser.Chart(Fixture("chart-IE00BK5BQT80.SG.json"))!.Closes.ShouldBeEmpty();
        YahooParser.Chart(Fixture("chart-not-found.json")).ShouldBeNull();
    }

    [Fact]
    public void Pence_and_missing_days_are_handled()
    {
        const string json = """
            {"chart":{"result":[{"meta":{"currency":"GBp","gmtoffset":3600},
              "timestamp":[1756710000,1756796400,1756882800],
              "indicators":{"quote":[{"close":[7512.5,null,7600]}]}}],"error":null}}
            """;

        var series = YahooParser.Chart(json)!;

        series.Currency.ShouldBe("GBP");
        series.Closes.ShouldBe([new DailyClose(new DateOnly(2025, 9, 1), 75.125m), new DailyClose(new DateOnly(2025, 9, 3), 76m)]);
    }

    [Fact]
    public async Task Resolution_prefers_the_brokers_listing_in_the_securitys_currency()
    {
        var handler = new RecordedYahoo();
        var source = new YahooPriceHistorySource(new HttpClient(handler), NullLogger<YahooPriceHistorySource>.Instance);

        var match = await source.ResolveAsync(new ListingQuery("IE00BK5BQT80", "VWCE", "EUR", [("VWCE", "XETR")]),
            CancellationToken.None);

        match.ShouldBe(new ListingMatch("VWCE.DE", "EUR"));
        handler.Paths.ShouldBe(["/v1/finance/search", "/v8/finance/chart/VWCE.DE"]);
        // Only the ISIN and the listing symbol leave the server.
        handler.Queries[0].ShouldContain("q=IE00BK5BQT80");
    }

    [Fact]
    public async Task Without_a_hint_a_listing_in_another_currency_is_accepted_after_skipping_empty_ones()
    {
        var handler = new RecordedYahoo();
        var source = new YahooPriceHistorySource(new HttpClient(handler), NullLogger<YahooPriceHistorySource>.Instance);

        var match = await source.ResolveAsync(new ListingQuery("IE00BK5BQT80", "VWCE", "EUR", []), CancellationToken.None);

        match.ShouldBe(new ListingMatch("VWRA.L", "USD"));
        handler.Paths.ShouldBe(["/v1/finance/search", "/v8/finance/chart/VWRA.L", "/v8/finance/chart/IE00BK5BQT80.SG"]);
    }

    [Fact]
    public async Task Provider_errors_return_nothing_instead_of_throwing()
    {
        var handler = new RecordedYahoo { Status = HttpStatusCode.TooManyRequests };
        var source = new YahooPriceHistorySource(new HttpClient(handler), NullLogger<YahooPriceHistorySource>.Instance);

        (await source.GetDailyClosesAsync("VWCE.DE", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 10),
            CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Only_search_and_chart_reads_are_allowed()
    {
        var recorded = new RecordedYahoo();
        var http = new HttpClient(new AllowListHttpHandler(YahooPriceHistorySource.AllowList) { InnerHandler = recorded });

        await http.GetAsync(new Uri($"https://{YahooPriceHistorySource.Host}/v8/finance/chart/VWCE.DE?interval=1d"));
        await Should.ThrowAsync<BlockedRequestException>(() =>
            http.GetAsync(new Uri($"https://{YahooPriceHistorySource.Host}/v7/finance/quote?symbols=X")));
        await Should.ThrowAsync<BlockedRequestException>(() =>
            http.PostAsync(new Uri($"https://{YahooPriceHistorySource.Host}/v1/finance/search"), null));
        await Should.ThrowAsync<BlockedRequestException>(() =>
            http.GetAsync(new Uri($"https://{YahooPriceHistorySource.Host}/v8/finance/chart/../../x")));
        recorded.Paths.Count.ShouldBe(1);
    }

    [Fact]
    public void Missing_ranges_extend_the_covered_interval_without_gaps()
    {
        var listing = PriceListing.Create(Guid.NewGuid(), "yahoo", "VWCE.DE", "EUR", DateTimeOffset.UnixEpoch);
        PriceHistoryService.Missing(listing, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31))
            .ShouldBe([(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31))]);

        listing.Cover(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
        PriceHistoryService.Missing(listing, new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 31)).ShouldBeEmpty();
        PriceHistoryService.Missing(listing, new DateOnly(2024, 12, 1), new DateOnly(2025, 6, 30)).ShouldBe(
        [
            (new DateOnly(2024, 12, 1), new DateOnly(2024, 12, 31)),
            (new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30)),
        ]);
        PriceHistoryService.Missing(listing, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30))
            .ShouldBe([(new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30))]);
    }

    /// <summary>Serves the recorded responses by path.</summary>
    private sealed class RecordedYahoo : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public List<string> Paths { get; } = [];

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Queries.Add(request.RequestUri.Query);
            var file = path switch
            {
                "/v1/finance/search" => "search-IE00BK5BQT80.json",
                "/v8/finance/chart/VWCE.DE" => "chart-VWCE.DE.json",
                "/v8/finance/chart/VWRA.L" => "chart-VWRA.L.json",
                "/v8/finance/chart/IE00BK5BQT80.SG" => "chart-IE00BK5BQT80.SG.json",
                _ => null,
            };
            var status = file is null ? HttpStatusCode.NotFound : Status;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(file is null ? Fixture("chart-not-found.json") : Fixture(file),
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
