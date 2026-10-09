using System.Net;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>
/// A broker account linked to an allocation bucket: the deposits its sync reports count towards that bucket each
/// month, and money also recorded in the ledger as going to that broker is counted once.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BrokerAllocationTests(ApiFactory factory)
{
    private static async Task WaitForSyncAsync(ApiClient api, Guid connectionId)
    {
        for (var i = 0; i < 120; i++)
        {
            var jobs = await api.GetAsync<JsonElement[]>($"/api/integrations/connections/{connectionId}/jobs");
            if (jobs.Any(j => j.GetProperty("outcome").GetString() != "Running"))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Sync did not finish.");
    }

    /// <summary>The demo broker A of the portfolio tests (created the same way when this test runs first).</summary>
    private static async Task<Guid> DemoBrokerAccountAsync(ApiClient api)
    {
        var connections = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
        if (connections.Length < 2)
        {
            foreach (var (name, profile) in new[] { ("Demo broker A", "a"), ("Demo broker B", "b") })
            {
                var id = await api.CreateAsync("/api/integrations/connections", new
                {
                    kind = "Demo", displayName = name, credentials = new Dictionary<string, string> { ["profile"] = profile },
                });
                await WaitForSyncAsync(api, id);
            }

            connections = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
        }

        var a = connections.First(c => c.GetProperty("displayName").GetString() == "Demo broker A");
        await WaitForSyncAsync(api, a.GetProperty("id").GetGuid());
        return a.GetProperty("accountId").GetGuid();
    }

    [Fact]
    public async Task Synced_deposits_count_towards_the_linked_bucket_once()
    {
        var api = await factory.OwnerAsync();
        var broker = await DemoBrokerAccountAsync(api);
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Broker-fed ETFs", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Broker allocation bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last = new DateOnly(today.Year, today.Month, 1).AddMonths(-1); // demo broker A deposits 600 every month
        var period = $"{last:yyyy-MM}";
        async Task<JsonElement> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{period}");
            return report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Single(b => b.GetProperty("bucketId").GetGuid() == bucket);
        }
        async Task Link(Guid? bucketId) => await ApiClient.EnsureAsync(
            await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId }));

        try
        {
            await Link(bucket);
            var line = await Line();
            line.GetProperty("actual").GetDecimal().ShouldBe(600m);
            line.GetProperty("fromBrokers").GetDecimal().ShouldBe(600m);

            // The same money recorded in the ledger as going to that broker: counted once, not 1,000.
            await api.CreateAsync("/api/transactions", new
            {
                type = "InvestmentContribution", occurredOn = $"{last:yyyy-MM}-02", amount = 400m, accountId = bank,
                bucketId = bucket, counterAccountId = broker,
            });
            line = await Line();
            line.GetProperty("actual").GetDecimal().ShouldBe(600m);
            line.GetProperty("fromBrokers").GetDecimal().ShouldBe(200m);

            // Unlinked: only the ledger counts.
            await Link(null);
            (await Line()).GetProperty("actual").GetDecimal().ShouldBe(400m);

            var account = (await api.GetAsync<JsonElement[]>("/api/accounts"))
                .Single(a => a.GetProperty("id").GetGuid() == broker);
            account.GetProperty("allocationBucketId").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId = (Guid?)null });
        }
    }

    [Fact]
    public async Task Only_broker_accounts_can_be_linked()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Not a broker", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Bank-fed", group = "Investment" });

        (await api.PutAsync($"/api/accounts/{bank}/allocation-bucket", new { bucketId = bucket })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }
}
