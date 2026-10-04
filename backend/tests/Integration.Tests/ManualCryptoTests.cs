using System.Net;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>Coins entered by hand flow into the portfolio, net worth and income like broker positions.</summary>
[Collection(ApiCollection.Name)]
public sealed class ManualCryptoTests(ApiFactory factory)
{
    private const decimal Close = 50_000m;
    private const decimal Live = Close * FakePriceHistory.CoinTodayFactor;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_coin_entered_by_hand_is_valued_edited_rewarded_and_removed()
    {
        var api = await factory.OwnerAsync();
        var before = await api.GetJsonAsync("/api/portfolio/summary");
        var netWorthBefore = (await api.GetJsonAsync("/api/net-worth")).GetProperty("current");

        var coins = await api.GetAsync<JsonElement[]>("/api/portfolio/manual/coins?q=bit");
        coins.ShouldContain(c => c.GetProperty("id").GetString() == "BTC");

        // Friendly refusals: nothing to price, nothing held, a duplicate.
        var noPrice = await api.PostAsync("/api/portfolio/manual", Body("NOPRICE", 1, 1, "Binance"));
        noPrice.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noPrice.Content.ReadAsStringAsync()).ShouldContain("No price was found");
        var zero = await api.PostAsync("/api/portfolio/manual", Body("BTC", 0, 1, "Binance"));
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await zero.Content.ReadAsStringAsync()).ShouldContain("how many coins");

        var id = await api.CreateAsync("/api/portfolio/manual", Body("BTC", 0.5m, 40_000m, "Binance", Today.AddDays(-30)));
        (await api.PostAsync("/api/portfolio/manual", Body("BTC", 1, 1, "binance"))).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);

        var manual = await api.GetJsonAsync("/api/portfolio/manual");
        var holding = manual.GetProperty("holdings").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == id);
        holding.GetProperty("location").GetString().ShouldBe("Binance");
        manual.GetProperty("locations").EnumerateArray().Select(l => l.GetString()).ShouldContain("Binance");
        var securityId = holding.GetProperty("securityId").GetGuid();
        var accountId = holding.GetProperty("accountId").GetGuid();

        var line = await PositionAsync(api, securityId);
        line.GetProperty("assetClass").GetString().ShouldBe("Crypto");
        line.GetProperty("quantity").GetDecimal().ShouldBe(0.5m);
        line.GetProperty("marketValueBase").GetDecimal().ShouldBe(0.5m * Live);
        line.GetProperty("costBase").GetDecimal().ShouldBe(20_000m);
        line.GetProperty("unrealizedPnlBase").GetDecimal().ShouldBe(0.5m * Live - 20_000m);
        // 24/7 market: today's change is against yesterday's close.
        line.GetProperty("dayChangeBase").GetDecimal().ShouldBe(0.5m * (Live - Close));
        var h0 = line.GetProperty("holdings")[0];
        h0.GetProperty("broker").GetString().ShouldBe("Manual");
        h0.GetProperty("accountName").GetString().ShouldBe("Binance");

        var summary = await api.GetJsonAsync("/api/portfolio/summary");
        Delta(summary, before, "totalValue").ShouldBe(0.5m * Live);
        Delta(summary, before, "netContributions").ShouldBe(20_000m);
        summary.GetProperty("accounts").EnumerateArray()
            .Single(a => a.GetProperty("accountId").GetGuid() == accountId)
            .GetProperty("broker").GetString().ShouldBe("Manual");
        var netWorth = (await api.GetJsonAsync("/api/net-worth")).GetProperty("current");
        Delta(netWorth, netWorthBefore, "investments").ShouldBe(0.5m * Live);

        // History since the purchase date is rebuilt from daily closes in the background.
        var series = 0;
        for (var i = 0; i < 40 && series < 30; i++)
        {
            await Task.Delay(250);
            series = (await api.GetJsonAsync($"/api/portfolio/performance?accountId={accountId}"))
                .GetProperty("series").GetArrayLength();
        }

        series.ShouldBeGreaterThanOrEqualTo(30);

        // Editing just updates the holding.
        (await api.PutAsync($"/api/portfolio/manual/{id}",
            new { quantity = 1m, averagePrice = 45_000m, location = "Binance", notes = "DCA" })).EnsureSuccessStatusCode();
        line = await PositionAsync(api, securityId);
        line.GetProperty("quantity").GetDecimal().ShouldBe(1m);
        line.GetProperty("costBase").GetDecimal().ShouldBe(45_000m);
        Delta(await api.GetJsonAsync("/api/portfolio/summary"), before, "netContributions").ShouldBe(45_000m);

        // A staking reward adds coins at zero cost and counts as income at that day's close.
        var dividendsBefore = before.GetProperty("dividends").GetDecimal();
        var rewardId = await api.CreateAsync($"/api/portfolio/manual/{id}/rewards",
            new { quantity = 0.1m, receivedOn = Today.AddDays(-10), kind = "Staking" });
        line = await PositionAsync(api, securityId);
        line.GetProperty("quantity").GetDecimal().ShouldBe(1.1m);
        line.GetProperty("costBase").GetDecimal().ShouldBe(45_000m);
        summary = await api.GetJsonAsync("/api/portfolio/summary");
        summary.GetProperty("dividends").GetDecimal().ShouldBe(dividendsBefore + 0.1m * Close);
        Delta(summary, before, "netContributions").ShouldBe(45_000m);
        var income = await api.GetJsonAsync($"/api/portfolio/dividends?accountId={accountId}");
        income.GetProperty("totalNetBase").GetDecimal().ShouldBe(0.1m * Close);
        income.GetProperty("items")[0].GetProperty("broker").GetString().ShouldBe("Manual");
        (await api.GetJsonAsync("/api/portfolio/manual")).GetProperty("holdings").EnumerateArray()
            .Single(h => h.GetProperty("id").GetGuid() == id).GetProperty("rewards")[0]
            .GetProperty("valueBase").GetDecimal().ShouldBe(0.1m * Close);

        (await api.Http.DeleteAsync($"/api/portfolio/manual/{id}/rewards/{rewardId}")).EnsureSuccessStatusCode();
        (await api.GetJsonAsync("/api/portfolio/summary")).GetProperty("dividends").GetDecimal()
            .ShouldBe(dividendsBefore);

        // Removing the last coin of a location removes the location from the portfolio.
        (await api.Http.DeleteAsync($"/api/portfolio/manual/{id}")).EnsureSuccessStatusCode();
        (await api.GetAsync<JsonElement[]>("/api/portfolio/positions"))
            .ShouldNotContain(p => p.GetProperty("securityId").GetGuid() == securityId);
        var after = await api.GetJsonAsync("/api/portfolio/summary");
        after.GetProperty("accounts").EnumerateArray().ShouldNotContain(a => a.GetProperty("accountId").GetGuid() == accountId);
        Delta(after, before, "totalValue").ShouldBe(0);
        Delta(after, before, "netContributions").ShouldBe(0);
    }

    private static object Body(string coin, decimal quantity, decimal averagePrice, string? location,
        DateOnly? heldSince = null) =>
        new { coinId = coin, symbol = coin, name = coin, quantity, averagePrice, location, heldSince };

    private static decimal Delta(JsonElement now, JsonElement then, string property) =>
        now.GetProperty(property).GetDecimal() - then.GetProperty(property).GetDecimal();

    private static async Task<JsonElement> PositionAsync(ApiClient api, Guid securityId) =>
        (await api.GetAsync<JsonElement[]>("/api/portfolio/positions"))
        .Single(p => p.GetProperty("securityId").GetGuid() == securityId);
}
