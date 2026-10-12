using System.Net;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[CollectionDefinition(Name)]
public sealed class DataResetCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "data-reset";
}

/// <summary>Starting fresh deletes what was entered or imported and keeps broker connections and categories.</summary>
[Collection(DataResetCollection.Name)]
public sealed class DataResetTests(ApiFactory factory)
{
    [Fact]
    public async Task Start_fresh_keeps_connections_and_categories_and_deletes_the_rest()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bank = await api.CreateAsync("/api/accounts", new
        {
            name = "Old bank", kind = "Bank", currency = "EUR", openingBalance = 1000m,
            openingBalanceOn = today.AddMonths(-2).ToString("yyyy-MM-dd"),
        });
        var categories = await api.GetAsync<JsonElement[]>("/api/categories");
        var groceries = categories.First(c => c.GetProperty("key").GetString() == "groceries").GetProperty("id").GetGuid();
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = today.ToString("yyyy-MM-dd"), amount = 42.5m, currency = "EUR", accountId = bank,
            categoryId = groceries, description = "Groceries",
        });
        await api.CreateAsync("/api/goals", new { name = "Trip", targetAmount = 1000m });
        await api.CreateAsync("/api/portfolio/manual", new
        {
            coinId = "BTC", symbol = "BTC", name = "BTC", quantity = 0.1m, averagePrice = 40_000m, location = "Ledger",
        });
        var connectionId = await api.CreateAsync("/api/integrations/connections", new
        {
            kind = "Demo", displayName = "Broker", credentials = new Dictionary<string, string> { ["profile"] = "a" },
        });
        var connection = (await api.GetAsync<JsonElement[]>("/api/integrations/connections"))
            .Single(c => c.GetProperty("id").GetGuid() == connectionId);
        var brokerAccount = connection.GetProperty("accountId").GetGuid();

        // Without the exact phrase nothing happens.
        (await api.PostAsync("/api/data/reset", new { confirm = "yes" })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await api.GetAsync<JsonElement[]>("/api/goals")).ShouldNotBeEmpty();

        var reset = await api.PostAsync("/api/data/reset", new { confirm = "reset-all-data" });
        await ApiClient.EnsureAsync(reset);

        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts?includeArchived=true");
        accounts.Select(a => a.GetProperty("id").GetGuid()).ShouldBe([brokerAccount]);
        (await api.GetAsync<JsonElement[]>("/api/goals")).ShouldBeEmpty();
        (await api.GetJsonAsync("/api/portfolio/manual")).GetProperty("holdings").GetArrayLength().ShouldBe(0);
        (await api.GetAsync<JsonElement[]>("/api/integrations/connections")).Length.ShouldBe(1);
        (await api.GetAsync<JsonElement[]>("/api/categories")).Length.ShouldBe(categories.Length);
    }
}
