using Integrations.Application.Contracts;
using Integrations.Infrastructure;
using Integrations.Infrastructure.Ibkr;
using Investments.Domain;
using SharedKernel.Http;

namespace Integrations.Tests;

public sealed class IbkrFlexTests
{
    private const string Base = "/AccountManagement/FlexWebService";

    private const string SendOk = """
        <FlexStatementResponse timestamp="25 September, 2026 09:00 AM EDT">
          <Status>Success</Status><ReferenceCode>987654321</ReferenceCode>
          <Url>https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService/GetStatement</Url>
        </FlexStatementResponse>
        """;

    private const string InProgress = """
        <FlexStatementResponse><Status>Warn</Status><ErrorCode>1019</ErrorCode>
        <ErrorMessage>Statement generation in progress. Please try again shortly.</ErrorMessage></FlexStatementResponse>
        """;

    private const string Statement = """
        <FlexQueryResponse queryName="personal-finance" type="AF">
          <FlexStatements count="1">
            <FlexStatement accountId="U0000000" fromDate="20250925" toDate="20260924" period="Custom">
              <AccountInformation accountId="U0000000" currency="EUR" />
              <ChangeInNAV endingValue="25300.50" />
              <CashReport><CashReportCurrency currency="BASE_SUMMARY" endingCash="300.50" /></CashReport>
              <EquitySummaryInBase>
                <EquitySummaryByReportDateInBase reportDate="20260922" cash="1300.50" total="25000.00" />
                <EquitySummaryByReportDateInBase reportDate="20260923" cash="300.50" total="25100.00" />
                <EquitySummaryByReportDateInBase reportDate="20260924" cash="300.50" total="25300.50" />
              </EquitySummaryInBase>
              <OpenPositions>
                <OpenPosition levelOfDetail="SUMMARY" conid="265598" symbol="AAPL" isin="US0378331005" description="APPLE INC"
                  currency="USD" assetCategory="STK" position="10" markPrice="200.00" costBasisPrice="150.00" listingExchange="NASDAQ" />
                <OpenPosition levelOfDetail="SUMMARY" conid="42" symbol="VWCE" isin="IE00BK5BQT80" description="VANGUARD FTSE ALL-WORLD"
                  currency="EUR" assetCategory="STK" subCategory="ETF" position="100" markPrice="120.00" costBasisPrice="100.00" />
                <OpenPosition levelOfDetail="LOT" conid="42" symbol="VWCE" position="50" markPrice="120.00" costBasisPrice="99.00" />
              </OpenPositions>
              <Trades>
                <Trade levelOfDetail="EXECUTION" tradeID="111" conid="42" symbol="VWCE" isin="IE00BK5BQT80" currency="EUR"
                  assetCategory="STK" subCategory="ETF" buySell="BUY" quantity="10" tradePrice="118.00" ibCommission="-1.25"
                  ibCommissionCurrency="EUR" taxes="0" netCash="-1181.25" fifoPnlRealized="0" fxRateToBase="1" dateTime="20260923;093005" />
                <Trade levelOfDetail="EXECUTION" tradeID="112" conid="1" symbol="EUR.USD" currency="USD" assetCategory="CASH"
                  buySell="SELL" quantity="-1000" tradePrice="1.1" netCash="1100" fxRateToBase="0.9" dateTime="20260923;100000" />
              </Trades>
              <CashTransactions>
                <CashTransaction levelOfDetail="DETAIL" type="Deposits/Withdrawals" transactionID="501" currency="EUR"
                  amount="1000" fxRateToBase="1" dateTime="20260923;080000" description="CASH RECEIPTS" />
                <CashTransaction levelOfDetail="DETAIL" type="Dividends" transactionID="601" actionID="9001" conid="265598"
                  symbol="AAPL" isin="US0378331005" description="AAPL CASH DIVIDEND" currency="USD" amount="2.50" dateTime="20260815" />
                <CashTransaction levelOfDetail="DETAIL" type="Withholding Tax" transactionID="602" actionID="9001" conid="265598"
                  symbol="AAPL" isin="US0378331005" currency="USD" amount="-0.38" dateTime="20260815" />
                <CashTransaction levelOfDetail="DETAIL" type="Other Fees" transactionID="701" currency="EUR" amount="-3"
                  dateTime="20260901" description="MARKET DATA" />
              </CashTransactions>
            </FlexStatement>
          </FlexStatements>
        </FlexQueryResponse>
        """;

    private const string Unavailable = """
        <FlexStatementResponse><Status>Fail</Status><ErrorCode>1003</ErrorCode>
        <ErrorMessage>Statement is not available.</ErrorMessage></FlexStatementResponse>
        """;

    private static IbkrFlexProvider Provider(ScriptedHandler http, int backfillYears = 1)
    {
        var clock = new InstantTimeProvider();
        var guard = new AllowListHttpHandler(FlexClient.AllowList()) { InnerHandler = http };
        return new IbkrFlexProvider(new FlexClient(new HttpClient(guard), new RateGate(clock), clock), "tok", "123",
            backfillYears, clock);
    }

    private static DateOnly ToDate(HttpRequestMessage r) => DateOnly.ParseExact(
        System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query)["td"]!, "yyyyMMdd",
        System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly FromDate(HttpRequestMessage r) => DateOnly.ParseExact(
        System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query)["fd"]!, "yyyyMMdd",
        System.Globalization.CultureInfo.InvariantCulture);

    private static ScriptedHandler Script() => new ScriptedHandler()
        .On($"{Base}/SendRequest", SendOk)
        .On($"{Base}/GetStatement", InProgress)
        .On($"{Base}/GetStatement", Statement);

    [Fact]
    public async Task A_backfill_window_before_the_account_existed_is_narrowed_instead_of_failing()
    {
        // Newest year: fine. Older year: 1003 for the full window, then fine once narrowed towards the present.
        var http = new ScriptedHandler()
            .On($"{Base}/SendRequest", SendOk)
            .On($"{Base}/SendRequest", Unavailable)
            .On($"{Base}/SendRequest", SendOk)
            .On($"{Base}/GetStatement", Statement);

        var positions = await Provider(http, backfillYears: 3).GetPositionsAsync(CancellationToken.None);

        positions.ShouldNotBeEmpty();
        var sends = http.Requests.Where(r => r.RequestUri!.AbsolutePath.EndsWith("SendRequest", StringComparison.Ordinal))
            .Select(FromDate).ToList();
        sends.Count.ShouldBe(3); // the third, oldest year is never requested: the account starts inside year two
        sends[0].ShouldBeGreaterThan(sends[1]); // newest window first
        sends[2].ShouldBeGreaterThan(sends[1]); // same window, narrowed
    }

    [Fact]
    public async Task Incremental_syncs_use_the_query_period_instead_of_a_short_recent_range()
    {
        var http = Script();

        var since = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero); // ten days before the test clock
        await Provider(http).GetTransactionsAsync(since, CancellationToken.None);

        var send = http.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("SendRequest", StringComparison.Ordinal));
        send.RequestUri!.Query.ShouldNotContain("fd=");
        send.RequestUri!.Query.ShouldNotContain("td=");
    }

    [Fact]
    public async Task The_newest_backfill_window_ends_earlier_when_the_last_day_is_not_closed_yet()
    {
        var http = new ScriptedHandler()
            .On($"{Base}/SendRequest", Unavailable)
            .On($"{Base}/SendRequest", SendOk)
            .On($"{Base}/GetStatement", Statement);

        await Provider(http).GetPositionsAsync(CancellationToken.None);

        var sends = http.Requests.Where(r => r.RequestUri!.AbsolutePath.EndsWith("SendRequest", StringComparison.Ordinal))
            .ToList();
        sends.Count.ShouldBe(2);
        FromDate(sends[1]).ShouldBe(FromDate(sends[0])); // same start
        ToDate(sends[1]).ShouldBe(ToDate(sends[0]).AddDays(-1)); // one day earlier end
    }

    [Fact]
    public async Task When_no_period_has_a_statement_the_error_explains_what_to_check()
    {
        var http = new ScriptedHandler().On($"{Base}/SendRequest", Unavailable);

        var error = await Should.ThrowAsync<ProviderConfigurationException>(
            () => Provider(http).GetPositionsAsync(CancellationToken.None));

        error.Message.ShouldContain("1003");
        error.Message.ShouldContain("Query ID");
        error.Message.ShouldContain("Last 365 Calendar Days");
        http.Requests.Count.ShouldBeLessThan(16); // a few earlier end dates, then halving: it gives up quickly
    }

    [Fact]
    public async Task Polls_until_the_statement_is_ready_and_reads_positions()
    {
        var http = Script();
        var provider = Provider(http);

        var positions = await provider.GetPositionsAsync(CancellationToken.None);

        http.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("GetStatement", StringComparison.Ordinal)).ShouldBe(2);
        http.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
        positions.Count.ShouldBe(2); // LOT rows are ignored
        var vwce = positions.Single(p => p.Security.Isin == "IE00BK5BQT80");
        vwce.Security.AssetClass.ShouldBe(AssetClass.Etf);
        vwce.Quantity.ShouldBe(100m);
        positions.Single(p => p.Security.Symbol == "AAPL").Security.BrokerSymbol.ShouldBe("265598");
    }

    [Fact]
    public async Task Reads_nav_history_with_deposits_as_flows()
    {
        var snapshot = await Provider(Script()).GetAccountSnapshotAsync(CancellationToken.None);

        snapshot.Currency.ShouldBe("EUR");
        snapshot.Cash.ShouldBe(300.50m);
        snapshot.TotalValue.ShouldBe(25300.50m);
        snapshot.History.Count.ShouldBe(3);
        snapshot.History.Single(h => h.Date == new DateOnly(2026, 9, 23)).NetFlow.ShouldBe(1000m);
    }

    [Fact]
    public async Task Trades_exclude_fx_conversions_and_dividends_pair_with_withholding()
    {
        var provider = Provider(Script());

        var tx = await provider.GetTransactionsAsync(null, CancellationToken.None);
        var dividends = await provider.GetDividendsAsync(null, CancellationToken.None);

        var trade = tx.Where(t => t.Trade is not null).Select(t => t.Trade!).Single();
        trade.ExternalId.ShouldBe("ibkr:trade:111");
        trade.Fees.ShouldBe(1.25m);
        trade.NetAmountInAccountCurrency.ShouldBe(-1181.25m);
        tx.Where(t => t.Cash is not null).Select(t => t.Cash!.Type)
            .ShouldBe([CashMovementType.Deposit, CashMovementType.Fee], ignoreOrder: true);

        var dividend = dividends.Single();
        dividend.GrossAmount.ShouldBe(2.50m);
        dividend.WithholdingTax.ShouldBe(0.38m);
        dividend.NetAmount.ShouldBe(2.12m);
    }

    [Fact]
    public async Task Expired_token_needs_user_attention()
    {
        var http = new ScriptedHandler().On($"{Base}/SendRequest",
            "<FlexStatementResponse><Status>Fail</Status><ErrorCode>1012</ErrorCode><ErrorMessage>Token has expired.</ErrorMessage></FlexStatementResponse>");

        var ex = await Should.ThrowAsync<ProviderConfigurationException>(() =>
            Provider(http).GetPositionsAsync(CancellationToken.None));
        ex.Message.ShouldContain("expired");
    }

    [Fact]
    public void Only_the_two_reporting_endpoints_are_allowed()
    {
        var allowed = FlexClient.AllowList().ToList();

        allowed.ShouldContain(a => a.Matches(HttpMethod.Get, new Uri("https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService/SendRequest")));
        allowed.ShouldNotContain(a => a.Matches(HttpMethod.Post, new Uri("https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService/SendRequest")));
        allowed.ShouldNotContain(a => a.Matches(HttpMethod.Get, new Uri("https://api.ibkr.com/v1/api/iserver/account/U1/orders")));
    }
}
