using System.Text.Json;
using Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class PortfolioTests(ApiFactory factory)
{
    private static async Task<JsonElement> WaitForJobAsync(ApiClient api, Guid connectionId, int expectedJobs)
    {
        for (var i = 0; i < 120; i++)
        {
            var jobs = await api.GetAsync<JsonElement[]>($"/api/integrations/connections/{connectionId}/jobs");
            var done = jobs.Where(j => j.GetProperty("outcome").GetString() != "Running").ToList();
            if (done.Count >= expectedJobs)
            {
                return done[0];
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Sync did not finish.");
    }

    private static async Task<(Guid A, Guid B)> ConnectDemoBrokersAsync(ApiClient api)
    {
        var existing = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
        if (existing.Length >= 2)
        {
            return (existing[0].GetProperty("id").GetGuid(), existing[1].GetProperty("id").GetGuid());
        }

        var a = await api.CreateAsync("/api/integrations/connections", new
        {
            kind = "Demo", displayName = "Demo broker A", credentials = new Dictionary<string, string> { ["profile"] = "a" },
        });
        var b = await api.CreateAsync("/api/integrations/connections", new
        {
            kind = "Demo", displayName = "Demo broker B", credentials = new Dictionary<string, string> { ["profile"] = "b" },
        });
        (await WaitForJobAsync(api, a, 1)).GetProperty("outcome").GetString().ShouldBe("Succeeded");
        (await WaitForJobAsync(api, b, 1)).GetProperty("outcome").GetString().ShouldBe("Succeeded");
        return (a, b);
    }

    [Fact]
    public async Task Demo_brokers_sync_into_a_consolidated_portfolio()
    {
        var api = await factory.OwnerAsync();
        await ConnectDemoBrokersAsync(api);

        var positions = await api.GetAsync<JsonElement[]>("/api/portfolio/positions");
        var vwce = positions.Single(p => p.GetProperty("isin").GetString() == "IE00BK5BQT80");
        // Same ISIN at two brokers: one consolidated line, two holdings.
        vwce.GetProperty("holdings").GetArrayLength().ShouldBe(2);
        vwce.GetProperty("quantity").GetDecimal().ShouldBe(
            vwce.GetProperty("holdings").EnumerateArray().Sum(h => h.GetProperty("quantity").GetDecimal()));
        positions.Sum(p => p.GetProperty("portfolioWeight").GetDecimal()).ShouldBeLessThanOrEqualTo(1.0001m);

        var summary = await api.GetJsonAsync("/api/portfolio/summary");
        summary.GetProperty("totalValue").GetDecimal().ShouldBeGreaterThan(0);
        summary.GetProperty("netContributions").GetDecimal().ShouldBeGreaterThan(0);
        summary.GetProperty("dividends").GetDecimal().ShouldBeGreaterThan(0);
        summary.GetProperty("accounts").GetArrayLength().ShouldBe(2);

        var perBroker = await api.GetJsonAsync("/api/portfolio/summary?broker=Demo");
        perBroker.GetProperty("totalValue").GetDecimal().ShouldBe(summary.GetProperty("totalValue").GetDecimal());

        var performance = await api.GetJsonAsync("/api/portfolio/performance");
        performance.GetProperty("timeWeightedReturn").ValueKind.ShouldBe(JsonValueKind.Number);
        performance.GetProperty("moneyWeightedReturn").ValueKind.ShouldBe(JsonValueKind.Number);
        performance.GetProperty("series").GetArrayLength().ShouldBeGreaterThan(100);

        var dividends = await api.GetJsonAsync("/api/portfolio/dividends");
        dividends.GetProperty("items")[0].GetProperty("withholdingTax").GetDecimal().ShouldBeGreaterThan(0);

        var netWorth = await api.GetJsonAsync("/api/net-worth");
        netWorth.GetProperty("current").GetProperty("investments").GetDecimal()
            .ShouldBe(summary.GetProperty("totalValue").GetDecimal());
    }

    [Fact]
    public async Task Repeated_syncs_never_duplicate_records()
    {
        var api = await factory.OwnerAsync();
        var (a, _) = await ConnectDemoBrokersAsync(api);
        var before = await api.GetJsonAsync("/api/portfolio/summary");

        (await api.PostAsync($"/api/integrations/connections/{a}/sync", new { })).EnsureSuccessStatusCode();
        var jobs = await api.GetAsync<JsonElement[]>($"/api/integrations/connections/{a}/jobs");
        var second = await WaitForJobAsync(api, a, jobs.Length + 1);

        second.GetProperty("imported").GetInt32().ShouldBe(0);
        var after = await api.GetJsonAsync("/api/portfolio/summary");
        after.GetProperty("netContributions").GetDecimal().ShouldBe(before.GetProperty("netContributions").GetDecimal());
        after.GetProperty("dividends").GetDecimal().ShouldBe(before.GetProperty("dividends").GetDecimal());
        after.GetProperty("fees").GetDecimal().ShouldBe(before.GetProperty("fees").GetDecimal());
    }

    [Fact]
    public async Task Credentials_are_write_only()
    {
        var api = await factory.OwnerAsync();
        await ConnectDemoBrokersAsync(api);

        var raw = await api.Http.GetStringAsync("/api/integrations/connections");

        raw.ShouldNotContain("\"profile\"");         // credential keys
        raw.ShouldNotContain("apiSecret", Case.Insensitive);
        raw.ShouldNotContain("protectedCredentials", Case.Insensitive);
        raw.ShouldNotContain("CfDJ8");                // Data Protection ciphertext prefix
    }

    [Fact]
    public async Task Broker_accounts_and_records_cannot_be_edited_by_hand()
    {
        var api = await factory.OwnerAsync();
        await ConnectDemoBrokersAsync(api);
        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        var broker = accounts.First(a => a.GetProperty("kind").GetString() == "Broker");

        var edit = await api.PutAsync($"/api/accounts/{broker.GetProperty("id").GetGuid()}",
            new { name = "Hacked", openingBalance = 1_000_000m, openingBalanceOn = "2026-01-01" });
        var expense = await api.PostAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2026-09-01", amount = 10m, accountId = broker.GetProperty("id").GetGuid(),
            categoryId = Finance.Domain.SystemCatalog.CategoryId("other"),
        });

        ((int)edit.StatusCode).ShouldBe(403);
        ((int)expense.StatusCode).ShouldBe(400);
    }

    /// <summary>Investment data is read-only over HTTP; only user preferences may be written.</summary>
    [Fact]
    public void Portfolio_endpoints_are_read_only_except_preferences()
    {
        var sources = factory.Services.GetServices<EndpointDataSource>();
        var writes = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/portfolio", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Where(x => !x.StartsWith("GET ", StringComparison.Ordinal))
            .Order()
            .ToList();

        // Coins entered by hand are the user's own records (no exchange or wallet is ever contacted).
        writes.Where(w => !w.Contains("/api/portfolio/manual", StringComparison.Ordinal))
            .ShouldBe(["PUT /api/portfolio/securities/{id:guid}/asset-class", "PUT /api/portfolio/targets"]);
        writes.Where(w => w.Contains("/api/portfolio/manual", StringComparison.Ordinal)).ShouldBe(
        [
            "DELETE /api/portfolio/manual/{id:guid}",
            "DELETE /api/portfolio/manual/{id:guid}/rewards/{rewardId:guid}",
            "POST /api/portfolio/manual/",
            "POST /api/portfolio/manual/{id:guid}/rewards",
            "POST /api/portfolio/manual/refresh-prices",
            "PUT /api/portfolio/manual/{id:guid}",
        ]);
    }
}
