using System.Net;
using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>
/// Shared expenses (ADR-0011): the payer's account loses the whole amount, spending counts only the owner's part, each
/// person owes their share until they pay it back (a transfer, neither income nor spending), and net worth counts what
/// is owed. Months of 2053 are used only here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SharedExpenseTests(ApiFactory factory)
{
    private static readonly Guid Subscriptions = SystemCatalog.CategoryId("subscriptions");

    private static async Task<decimal> SpentAsync(ApiClient api, string period) =>
        (await api.GetAsync<JsonElement[]>($"/api/reports/categories/{period}"))
        .SingleOrDefault(c => c.GetProperty("key").GetString() == "subscriptions") is { ValueKind: JsonValueKind.Object } c
            ? c.GetProperty("actual").GetDecimal()
            : 0m;

    private static async Task<decimal> BalanceAsync(ApiClient api, Guid account) =>
        (await api.GetAsync<JsonElement[]>("/api/accounts")).Single(a => a.GetProperty("id").GetGuid() == account)
        .GetProperty("balance").GetDecimal();

    private static async Task<JsonElement> PersonAsync(ApiClient api, Guid person) =>
        (await api.GetJsonAsync("/api/sharing")).GetProperty("people").EnumerateArray()
        .Single(p => p.GetProperty("id").GetGuid() == person);

    [Fact]
    public async Task A_shared_expense_counts_only_the_owners_part_and_each_person_owes_theirs_until_paid()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Shared card", kind = "Bank", currency = "EUR", openingBalance = 100m });
        var ana = await api.CreateAsync("/api/accounts", new { name = "Ana (shared test)", kind = "Person", currency = "EUR", openingBalance = 0m });
        var rui = await api.CreateAsync("/api/accounts", new { name = "Rui (shared test)", kind = "Person", currency = "EUR", openingBalance = 0m });

        var netflix = await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2053-07-05", amount = 18m, accountId = bank, categoryId = Subscriptions,
            description = "Streaming", shares = new[] { new { personId = ana, amount = 6m }, new { personId = rui, amount = 6m } },
        });

        (await BalanceAsync(api, bank)).ShouldBe(82m); // the whole amount left the card
        (await SpentAsync(api, "2053-07")).ShouldBe(6m); // only the owner's part is spending
        (await BalanceAsync(api, ana)).ShouldBe(6m);
        var expense = await api.GetJsonAsync($"/api/transactions/{netflix}");
        expense.GetProperty("shares").GetArrayLength().ShouldBe(2);

        // Ana owes her part; the main list shows the expense, not the shares.
        var person = await PersonAsync(api, ana);
        person.GetProperty("balance").GetDecimal().ShouldBe(6m);
        person.GetProperty("open").EnumerateArray().Single().GetProperty("outstanding").GetDecimal().ShouldBe(6m);
        var list = await api.GetJsonAsync("/api/transactions?from=2053-07-01&to=2053-07-31");
        list.GetProperty("items").EnumerateArray().ShouldAllBe(t => t.GetProperty("id").GetGuid() == netflix);

        // Paid back: a transfer into the bank, neither income nor spending.
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/sharing/{ana}/settle", new
        {
            direction = "received", accountId = bank, amount = 6m, on = "2053-07-09",
        }));
        (await BalanceAsync(api, bank)).ShouldBe(88m);
        (await SpentAsync(api, "2053-07")).ShouldBe(6m);
        (await GetIncomeAsync(api)).ShouldBe(0m);
        person = await PersonAsync(api, ana);
        person.GetProperty("balance").GetDecimal().ShouldBe(0m);
        person.GetProperty("open").GetArrayLength().ShouldBe(0);

        // Editing the expense moves the shares with it; a share is not edited on its own.
        await ApiClient.EnsureAsync(await api.PutAsync($"/api/transactions/{netflix}", new
        {
            type = "Expense", occurredOn = "2053-07-06", amount = 21m, accountId = bank, categoryId = Subscriptions,
            description = "Streaming", shares = new[] { new { personId = ana, amount = 7m }, new { personId = rui, amount = 7m } },
        }));
        (await SpentAsync(api, "2053-07")).ShouldBe(7m);
        var ruiShare = (await api.GetJsonAsync($"/api/transactions/{netflix}")).GetProperty("shares").EnumerateArray()
            .Single(s => s.GetProperty("personId").GetGuid() == rui).GetProperty("transactionId").GetGuid();
        (await api.PutAsync($"/api/transactions/{ruiShare}", new
        {
            type = "Expense", occurredOn = "2053-07-06", amount = 1m, accountId = rui, categoryId = Subscriptions, isRefund = true,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Deleting one share: the owner takes that part on.
        (await api.Http.DeleteAsync($"/api/transactions/{ruiShare}")).EnsureSuccessStatusCode();
        (await SpentAsync(api, "2053-07")).ShouldBe(14m);
        (await BalanceAsync(api, rui)).ShouldBe(0m);

        // Deleting the expense takes its shares with it; restoring brings them back.
        (await api.Http.DeleteAsync($"/api/transactions/{netflix}")).EnsureSuccessStatusCode();
        (await BalanceAsync(api, ana)).ShouldBe(-6m); // she paid 6 for a share that no longer exists
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/transactions/{netflix}/restore", new { }));
        (await BalanceAsync(api, ana)).ShouldBe(1m);
        (await SpentAsync(api, "2053-07")).ShouldBe(14m);
    }

    [Fact]
    public async Task Shares_must_be_people_within_the_amount_of_a_single_category_expense()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Shared rules bank", kind = "Bank", currency = "EUR", openingBalance = 0m });
        var other = await api.CreateAsync("/api/accounts", new { name = "Shared rules savings", kind = "Savings", currency = "EUR", openingBalance = 0m });
        var ana = await api.CreateAsync("/api/accounts", new { name = "Ana (rules)", kind = "Person", currency = "EUR", openingBalance = 0m });

        async Task<HttpStatusCode> Post(object shares, object? splits = null, Guid? account = null) =>
            (await api.PostAsync("/api/transactions", new
            {
                type = "Expense", occurredOn = "2053-08-01", amount = 10m, accountId = account ?? bank,
                categoryId = splits is null ? Subscriptions : (Guid?)null, splits, shares,
            })).StatusCode;

        (await Post(new[] { new { personId = ana, amount = 11m } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new[] { new { personId = other, amount = 5m } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new[] { new { personId = ana, amount = 2m }, new { personId = ana, amount = 2m } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new[] { new { personId = ana, amount = 5m } },
            new[] { new { categoryId = Subscriptions, amount = 5m }, new { categoryId = SystemCatalog.CategoryId("groceries"), amount = 5m } }))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new[] { new { personId = ana, amount = 5m } }, account: ana)).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new[] { new { personId = ana, amount = 10m } })).ShouldBe(HttpStatusCode.Created); // paid for her entirely
    }

    [Fact]
    public async Task A_friend_who_paid_for_the_owner_is_owed_until_paid_back()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Owed bank", kind = "Bank", currency = "EUR", openingBalance = 50m });
        var rita = await api.CreateAsync("/api/accounts", new { name = "Rita (owed test)", kind = "Person", currency = "EUR", openingBalance = 0m });

        // Rita paid the owner's 15 € of groceries.
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2053-09-03", amount = 15m, accountId = rita, categoryId = SystemCatalog.CategoryId("groceries"),
        });
        var person = await PersonAsync(api, rita);
        person.GetProperty("balance").GetDecimal().ShouldBe(-15m);
        (await api.GetJsonAsync("/api/sharing")).GetProperty("youOwe").GetDecimal().ShouldBeGreaterThanOrEqualTo(15m);

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/sharing/{rita}/settle", new
        {
            direction = "paid", accountId = bank, amount = 15m, on = "2053-09-04",
        }));
        (await PersonAsync(api, rita)).GetProperty("balance").GetDecimal().ShouldBe(0m);
        (await BalanceAsync(api, bank)).ShouldBe(35m);
        (await api.PostAsync($"/api/sharing/{rita}/settle", new { direction = "gift", accountId = bank, amount = 1m, on = "2053-09-04" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Net_worth_counts_what_people_owe_and_what_is_owed_to_them()
    {
        var api = await factory.OwnerAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        var bank = await api.CreateAsync("/api/accounts", new { name = "Net worth shared bank", kind = "Bank", currency = "EUR", openingBalance = 0m });
        var owes = await api.CreateAsync("/api/accounts", new { name = "Owes me (net worth)", kind = "Person", currency = "EUR", openingBalance = 0m });
        var owed = await api.CreateAsync("/api/accounts", new { name = "I owe (net worth)", kind = "Person", currency = "EUR", openingBalance = 0m });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = yesterday, amount = 30m, accountId = bank, categoryId = Subscriptions,
            shares = new[] { new { personId = owes, amount = 10m } },
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = yesterday, amount = 4m, accountId = owed, categoryId = Subscriptions,
        });

        var lines = (await api.GetJsonAsync("/api/net-worth")).GetProperty("current").GetProperty("lines").EnumerateArray().ToList();
        var receivable = lines.Single(l => l.GetProperty("name").GetString() == "Owes me (net worth)");
        receivable.GetProperty("group").GetString().ShouldBe("receivable");
        receivable.GetProperty("value").GetDecimal().ShouldBe(10m);
        var liability = lines.Single(l => l.GetProperty("name").GetString() == "I owe (net worth)");
        liability.GetProperty("group").GetString().ShouldBe("liability");
        liability.GetProperty("value").GetDecimal().ShouldBe(4m);
    }

    private static async Task<decimal> GetIncomeAsync(ApiClient api) =>
        (await api.GetJsonAsync("/api/reports/monthly/2053-07")).GetProperty("current").GetProperty("income").GetDecimal();
}
