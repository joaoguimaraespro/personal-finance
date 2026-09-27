using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class InvestmentEntryTests(ApiFactory factory)
{
    [Fact]
    public async Task Investments_are_reported_apart_from_everyday_income_and_expenses()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Investing bank 2033", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var stocks = SystemCatalog.BucketId("stocks-etfs");

        async Task Add(object body) => await api.CreateAsync("/api/transactions", body);
        await Add(new { type = "Income", occurredOn = "2033-05-01", amount = 3000m, accountId = bank, categoryId = SystemCatalog.CategoryId("salary") });
        await Add(new { type = "Expense", occurredOn = "2033-05-03", amount = 400m, accountId = bank, categoryId = SystemCatalog.CategoryId("groceries") });
        await Add(new
        {
            type = "InvestmentContribution", occurredOn = "2033-05-10", amount = 1000m, accountId = bank, bucketId = stocks,
            asset = new { kind = "Etf", symbol = "VWCE", name = "Vanguard FTSE All-World", quantity = 8m, unitPrice = 125m, priceSource = "Manual" },
        });
        await Add(new
        {
            type = "InvestmentSale", occurredOn = "2033-05-20", amount = 300m, accountId = bank, bucketId = stocks,
            asset = new { kind = "Stock", symbol = "AAPL", quantity = 1.5m, unitPrice = 200m },
        });

        var overview = await api.GetJsonAsync("/api/reports/overview/2033");
        var everyday = overview.GetProperty("everyday");
        everyday.GetProperty("income").GetDecimal().ShouldBe(3000m);
        everyday.GetProperty("expenses").GetDecimal().ShouldBe(400m);
        everyday.GetProperty("netBalance").GetDecimal().ShouldBe(2600m);
        var investments = overview.GetProperty("investments");
        investments.GetProperty("purchases").GetDecimal().ShouldBe(1000m);
        investments.GetProperty("sales").GetDecimal().ShouldBe(300m);
        investments.GetProperty("netInvested").GetDecimal().ShouldBe(700m);

        var page = await api.GetJsonAsync("/api/transactions?flow=Investment&from=2033-01-01&to=2033-12-31");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("type").GetString()).ShouldBe(["InvestmentSale", "InvestmentContribution"]);
        items[1].GetProperty("asset").GetProperty("symbol").GetString().ShouldBe("VWCE");
        items[1].GetProperty("flow").GetString().ShouldBe("Investment");

        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        accounts.Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal()
            .ShouldBe(3000m - 400m - 1000m + 300m);
    }

    [Fact]
    public async Task Instrument_search_without_broker_keys_degrades_gracefully()
    {
        var api = await factory.OwnerAsync();

        var result = await api.GetJsonAsync("/api/instruments/search?q=AAPL");
        result.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        var providers = result.GetProperty("providers").EnumerateArray().ToList();
        providers.Single(p => p.GetProperty("provider").GetString() == "Trading212")
            .GetProperty("configured").GetBoolean().ShouldBeFalse();

        var quote = await api.GetJsonAsync("/api/instruments/quote?provider=Trading212&symbol=AAPL_US_EQ&date=2033-05-10");
        quote.GetProperty("quote").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
