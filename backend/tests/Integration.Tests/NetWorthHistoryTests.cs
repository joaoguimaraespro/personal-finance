using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Integration.Tests.Infrastructure;
using Investments.Application.Portfolio;
using Investments.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

[CollectionDefinition(Name)]
public sealed class NetWorthHistoryCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "net-worth-history";
}

/// <summary>Before daily snapshots exist, net worth is rebuilt month by month from balances and broker history.</summary>
[Collection(NetWorthHistoryCollection.Name)]
public sealed class NetWorthHistoryTests(ApiFactory factory)
{
    [Fact]
    public async Task Months_before_the_first_snapshot_come_from_balances_and_broker_valuations()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var endThreeAgo = thisMonth.AddMonths(-2).AddDays(-1); // end of the month three months back
        var endTwoAgo = thisMonth.AddMonths(-1).AddDays(-1);
        var bank = await api.CreateAsync("/api/accounts", new
        {
            name = "Bank", kind = "Bank", currency = "EUR", openingBalance = 1000m,
            openingBalanceOn = endThreeAgo.AddDays(-5).ToString("yyyy-MM-dd"),
        });
        var salary = (await api.GetAsync<JsonElement[]>("/api/categories"))
            .First(c => c.GetProperty("key").GetString() == "salary").GetProperty("id").GetGuid();
        await api.CreateAsync("/api/transactions", new
        {
            categoryId = salary,
            type = "Income", occurredOn = endTwoAgo.AddDays(-3).ToString("yyyy-MM-dd"), amount = 500m,
            currency = "EUR", accountId = bank, description = "Salary",
        });

        // A broker account valued 2,000 at the end of the month three months back (its own history).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            var broker = Account.Create("Broker", AccountKind.Broker, "EUR", 0m, endThreeAgo, "IBKR");
            finance.Accounts.Add(broker);
            await finance.SaveChangesAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<PortfolioSyncWriter>().UpsertSnapshotsAsync(broker.Id,
                [new SnapshotReport(endThreeAgo, 2000m, 0m, "EUR", null)], SnapshotOrigins.Broker,
                CancellationToken.None);
        }

        var history = await api.GetJsonAsync("/api/net-worth");
        var series = history.GetProperty("series").EnumerateArray()
            .ToDictionary(p => DateOnly.Parse(p.GetProperty("date").GetString()!), p => p.GetProperty("netWorth").GetDecimal());

        series[endThreeAgo].ShouldBe(3000m); // 1,000 in the bank + 2,000 at the broker
        series[endTwoAgo].ShouldBe(3500m); // + the salary; the broker keeps its last value
        history.GetProperty("startDate").GetString().ShouldBe(endThreeAgo.ToString("yyyy-MM-dd"));
    }
}
