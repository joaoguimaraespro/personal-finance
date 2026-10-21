using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Tests.Infrastructure;
using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

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
        // Demo broker A deposits 600 every month; two months back is older than the review window, so it counts as is.
        var last = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
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
    public async Task A_recent_deposit_waits_for_the_account_it_came_from()
    {
        var api = await factory.OwnerAsync();
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Reviewed ETFs", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Deposit source bank", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var kind = $"DEPOSIT-{Guid.NewGuid():N}"[..20];
        Guid broker;
        using (var scope = factory.Services.CreateScope())
        {
            // A broker account of its own (as a connection creates it, without positions) and the deposit a sync
            // records when the money arrives today: no other test sees either.
            var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var account = Finance.Domain.Accounts.Account.Create("Reviewed broker", Finance.Domain.Accounts.AccountKind.Broker,
                "EUR", 0m, today.AddYears(-1));
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(CancellationToken.None);
            broker = account.Id;
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            db.CashMovements.Add(CashMovement.Record(broker, CashMovementType.Deposit, 500m, "EUR", 500m, now, kind,
                DataSource.Demo, $"test:{kind}", now));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        async Task<JsonElement?> Movement() => (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
            .Cast<JsonElement?>().SingleOrDefault(m => m!.Value.GetProperty("kind").GetString() == kind);
        async Task<(decimal Actual, decimal Pending)> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
            var line = report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Cast<JsonElement?>().SingleOrDefault(b => b!.Value.GetProperty("bucketId").GetGuid() == bucket);
            return line is { } l
                ? (l.GetProperty("actual").GetDecimal(), l.GetProperty("pending").GetDecimal())
                : (0m, 0m);
        }
        async Task<bool> Notified(Guid id) => (await api.GetJsonAsync("/api/notifications")).GetProperty("items")
            .EnumerateArray().Any(i => i.GetProperty("kind").GetString() == "BrokerMovement" &&
                                       i.GetProperty("targetId").GetGuid() == id);
        async Task<decimal> BankBalance() => (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal();

        try
        {
            await ApiClient.EnsureAsync(
                await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId = bucket }));
            var movement = (await Movement())!.Value;
            var id = movement.GetProperty("id").GetGuid();
            movement.GetProperty("state").GetString().ShouldBe("Pending");

            // Not counted until the owner says where the money came from: shown apart, and asked about.
            (await Line()).ShouldBe((0m, 500m));
            (await Notified(id)).ShouldBeTrue();

            // Recorded from the bank, for less (part of it went elsewhere): the bucket counts what was recorded.
            var response = await api.PostAsync($"/api/broker-movements/{id}/record", new
            {
                type = "InvestmentContribution", occurredOn = $"{today:yyyy-MM-dd}", amount = 487.01m, accountId = bank,
            });
            await ApiClient.EnsureAsync(response);
            var transactionId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await Line()).ShouldBe((487.01m, 0m));
            (await BankBalance()).ShouldBe(512.99m);
            (await Notified(id)).ShouldBeFalse();
            movement = (await Movement())!.Value;
            movement.GetProperty("state").GetString().ShouldBe("Recorded");
            movement.GetProperty("transactionId").GetGuid().ShouldBe(transactionId);
            var tx = await api.GetJsonAsync($"/api/transactions/{transactionId}");
            tx.GetProperty("counterAccountId").GetGuid().ShouldBe(broker);
            tx.GetProperty("bucketId").GetGuid().ShouldBe(bucket);

            // Recording it twice is refused.
            (await api.PostAsync($"/api/broker-movements/{id}/record", new
            {
                type = "InvestmentContribution", occurredOn = $"{today:yyyy-MM-dd}", amount = 1m, accountId = bank,
            })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // Its transaction deleted: the question comes back.
            await ApiClient.EnsureAsync(await api.Http.DeleteAsync($"/api/transactions/{transactionId}"));
            (await Movement())!.Value.GetProperty("state").GetString().ShouldBe("Pending");
            (await Notified(id)).ShouldBeTrue();

            // Ignored (e.g. already recorded by hand): neither counted nor asked about.
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{id}/ignore", new { }));
            (await Movement())!.Value.GetProperty("state").GetString().ShouldBe("Ignored");
            (await Line()).ShouldBe((0m, 0m));
            (await Notified(id)).ShouldBeFalse();
        }
        finally
        {
            // Gone again, so the portfolio tests see only their own brokers.
            using var scope = factory.Services.CreateScope();
            var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            await db.CashMovements.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
            await db.PortfolioSnapshots.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
            await finance.Transactions.IgnoreQueryFilters()
                .Where(t => t.AccountId == broker || t.CounterAccountId == broker).ExecuteDeleteAsync();
            await finance.Accounts.Where(a => a.Id == broker).ExecuteDeleteAsync();
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
