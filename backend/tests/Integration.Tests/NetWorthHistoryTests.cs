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

    [Fact]
    public async Task Savings_without_a_destination_are_set_aside_and_stay_in_the_account()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bank = await api.CreateAsync("/api/accounts", new
        {
            name = "Everyday bank", kind = "Bank", currency = "EUR", openingBalance = 1000m,
            openingBalanceOn = today.AddDays(-10).ToString("yyyy-MM-dd"),
        });
        var travel = (await api.GetAsync<JsonElement[]>("/api/buckets"))
            .First(b => b.GetProperty("key").GetString() == "travel-fund").GetProperty("id").GetGuid();
        await api.CreateAsync("/api/transactions", new
        {
            type = "Savings", occurredOn = today.ToString("yyyy-MM-dd"), amount = 200m, currency = "EUR",
            accountId = bank, bucketId = travel, description = "Set aside for the trip",
        });

        var account = (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == bank);
        account.GetProperty("balance").GetDecimal().ShouldBe(1000m); // the money did not leave the account
        var month = await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
        month.GetProperty("current").GetProperty("saved").GetDecimal().ShouldBe(200m); // but it counts as saved
    }

    [Fact]
    public async Task A_refund_lowers_spending_and_capital_repaid_shows_in_the_month()
    {
        var api = await factory.OwnerAsync();
        var day = new DateOnly(2031, 3, 10); // a month no other test touches
        var bank = await api.CreateAsync("/api/accounts", new
        {
            name = "Refund bank", kind = "Bank", currency = "EUR", openingBalance = 1000m,
            openingBalanceOn = day.AddDays(-5).ToString("yyyy-MM-dd"),
        });
        var loan = await api.CreateAsync("/api/accounts", new
        {
            name = "Car loan", kind = "Loan", currency = "EUR", openingBalance = -5000m,
            openingBalanceOn = day.AddDays(-5).ToString("yyyy-MM-dd"),
        });
        var groceries = (await api.GetAsync<JsonElement[]>("/api/categories"))
            .First(c => c.GetProperty("key").GetString() == "groceries").GetProperty("id").GetGuid();
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = day.ToString("yyyy-MM-dd"), amount = 100m, currency = "EUR",
            accountId = bank, categoryId = groceries, description = "Shopping",
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", isRefund = true, occurredOn = day.AddDays(2).ToString("yyyy-MM-dd"), amount = 30m,
            currency = "EUR", accountId = bank, categoryId = groceries, description = "Returned an item",
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Transfer", occurredOn = day.AddDays(3).ToString("yyyy-MM-dd"), amount = 200m, currency = "EUR",
            accountId = bank, counterAccountId = loan, description = "Capital",
        });

        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        accounts.Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal()
            .ShouldBe(1000m - 100m + 30m - 200m);
        var month = (await api.GetJsonAsync("/api/reports/monthly/2031-03")).GetProperty("current");
        month.GetProperty("totalExpenses").GetDecimal().ShouldBe(70m); // the refund lowers spending
        month.GetProperty("income").GetDecimal().ShouldBe(0m); // and is not income
        month.GetProperty("debtRepaid").GetDecimal().ShouldBe(200m);
        month.GetProperty("freeCashFlow").GetDecimal().ShouldBe(-70m - 200m);
    }
}
