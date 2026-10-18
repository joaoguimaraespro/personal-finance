using System.Net;
using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>Deleting an account for good: it takes its movements, recurring items and shares with it. Months of 2054.</summary>
[Collection(ApiCollection.Name)]
public sealed class AccountDeletionTests(ApiFactory factory)
{
    private static readonly Guid Subscriptions = SystemCatalog.CategoryId("subscriptions");

    private static async Task<decimal> SpentAsync(ApiClient api) =>
        (await api.GetAsync<JsonElement[]>("/api/reports/categories/2054-02"))
        .SingleOrDefault(c => c.GetProperty("key").GetString() == "subscriptions") is { ValueKind: JsonValueKind.Object } c
            ? c.GetProperty("actual").GetDecimal()
            : 0m;

    [Fact]
    public async Task Deleting_a_person_or_an_account_takes_what_belongs_to_it()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Delete me bank", kind = "Bank", currency = "EUR", openingBalance = 0m });
        var other = await api.CreateAsync("/api/accounts", new { name = "Keep me bank", kind = "Bank", currency = "EUR", openingBalance = 0m });
        var zoe = await api.CreateAsync("/api/accounts", new { name = "Zoe (delete)", kind = "Person", currency = "EUR", openingBalance = 0m });
        var expense = await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2054-02-03", amount = 30m, accountId = bank, categoryId = Subscriptions,
            shares = new[] { new { personId = zoe, amount = 10m } },
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Transfer", occurredOn = "2054-02-04", amount = 5m, accountId = other, counterAccountId = bank,
        });
        var shared = await api.CreateAsync("/api/recurring", new
        {
            name = "Shared with Zoe", type = "Expense", amount = 20m, accountId = other, frequency = "Monthly",
            startOn = "2054-03-01", categoryId = Subscriptions, shares = new[] { new { personId = zoe, percent = 50m } },
        });
        await api.CreateAsync("/api/recurring", new
        {
            name = "From the deleted bank", type = "Expense", amount = 9m, accountId = bank, frequency = "Monthly",
            startOn = "2054-03-01", categoryId = Subscriptions,
        });
        (await SpentAsync(api)).ShouldBe(20m);

        // The person: their share goes, so the expense is all the owner's; the recurring item is no longer shared.
        var preview = await api.GetJsonAsync($"/api/accounts/{zoe}/deletion");
        preview.GetProperty("transactions").GetInt32().ShouldBe(1);
        (await api.Http.DeleteAsync($"/api/accounts/{zoe}")).EnsureSuccessStatusCode();
        (await SpentAsync(api)).ShouldBe(30m);
        (await api.GetAsync<JsonElement[]>("/api/recurring")).Single(r => r.GetProperty("id").GetGuid() == shared)
            .GetProperty("shares").GetArrayLength().ShouldBe(0);
        (await api.GetJsonAsync($"/api/transactions/{expense}")).GetProperty("shares").GetArrayLength().ShouldBe(0);

        // The bank: its movements, the transfer into it and its recurring item go.
        preview = await api.GetJsonAsync($"/api/accounts/{bank}/deletion");
        preview.GetProperty("transactions").GetInt32().ShouldBe(2);
        preview.GetProperty("recurring").GetInt32().ShouldBe(1);
        (await api.Http.DeleteAsync($"/api/accounts/{bank}")).EnsureSuccessStatusCode();
        (await SpentAsync(api)).ShouldBe(0m);
        (await api.Http.GetAsync($"/api/transactions/{expense}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.GetAsync<JsonElement[]>("/api/accounts?includeArchived=true"))
            .ShouldNotContain(a => a.GetProperty("id").GetGuid() == bank);
        var balance = (await api.GetAsync<JsonElement[]>("/api/accounts")).Single(a => a.GetProperty("id").GetGuid() == other)
            .GetProperty("balance").GetDecimal();
        balance.ShouldBe(0m); // the transfer to the deleted bank is gone too
        (await api.GetAsync<JsonElement[]>("/api/recurring"))
            .ShouldNotContain(r => r.GetProperty("name").GetString() == "From the deleted bank");
        (await api.Http.DeleteAsync($"/api/accounts/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
