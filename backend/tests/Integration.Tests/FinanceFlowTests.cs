using System.Net;
using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class FinanceFlowTests(ApiFactory factory)
{
    private static readonly Guid Restaurants = SystemCatalog.CategoryId("restaurants");
    private static readonly Guid Salary = SystemCatalog.CategoryId("salary");
    private static readonly Guid Rent = SystemCatalog.CategoryId("housing");

    [Fact]
    public async Task Quick_add_expense_edit_delete_restore_is_fully_audited()
    {
        var api = await factory.OwnerAsync();
        var revolut = await api.CreateAsync("/api/accounts",
            new { name = "Revolut", kind = "Bank", currency = "EUR", openingBalance = 100m, institution = "Revolut" });

        var id = await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2026-09-25", amount = 32.50m, accountId = revolut,
            categoryId = Restaurants, description = "Dinner",
        });

        var created = await api.GetJsonAsync($"/api/transactions/{id}");
        created.GetProperty("nature").GetString().ShouldBe("Variable");
        created.GetProperty("categoryKey").GetString().ShouldBe("restaurants");
        created.GetProperty("source").GetString().ShouldBe("Manual");

        (await api.PutAsync($"/api/transactions/{id}", new
        {
            type = "Expense", occurredOn = "2026-09-25", amount = 45.90m, accountId = revolut,
            categoryId = Restaurants, description = "Dinner with friends",
        })).EnsureSuccessStatusCode();
        (await api.Http.DeleteAsync($"/api/transactions/{id}")).EnsureSuccessStatusCode();
        (await api.Http.GetAsync($"/api/transactions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.PostAsync($"/api/transactions/{id}/restore", new { })).EnsureSuccessStatusCode();

        var history = await api.GetAsync<JsonElement[]>($"/api/transactions/{id}/history");
        history.Select(h => h.GetProperty("action").GetString())
            .ShouldBe(["Created", "Updated", "Deleted", "Restored"]);
        history[1].GetProperty("changes").GetString()!.ShouldContain("45.90");
        history[0].GetProperty("actor").GetString().ShouldBe("user:" + OwnerSession.Email);

        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        accounts.Single(a => a.GetProperty("id").GetGuid() == revolut).GetProperty("balance").GetDecimal()
            .ShouldBe(100m - 45.90m);
    }

    [Fact]
    public async Task Category_type_mismatches_are_rejected()
    {
        var api = await factory.OwnerAsync();
        var account = await api.CreateAsync("/api/accounts", new { name = "Cash", kind = "Cash", currency = "EUR", openingBalance = 0 });

        var response = await api.PostAsync("/api/transactions",
            new { type = "Expense", occurredOn = "2026-01-10", amount = 10m, accountId = account, categoryId = Salary });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Identifiers_are_masked_in_responses()
    {
        var api = await factory.OwnerAsync();
        var id = await api.CreateAsync("/api/accounts", new
        {
            name = "Main", kind = "Bank", currency = "EUR", openingBalance = 0, identifier = "PT50000201231234567890154",
        });

        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        var raw = JsonSerializer.Serialize(accounts);
        raw.ShouldNotContain("PT5000020123");
        accounts.Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("identifierMasked").GetString()
            .ShouldBe("••••0154");
    }

    [Fact]
    public async Task Monthly_report_matches_spreadsheet_definitions()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Report bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        const string period = "2031-03";

        (await api.PutAsync("/api/budgets/2031-01", new
        {
            items = new object[]
            {
                new { target = "Bucket", mode = "PercentOfIncome", value = 0.25m, bucketId = SystemCatalog.BucketId("stocks-etfs") },
                new { target = "Bucket", mode = "PercentOfIncome", value = 0.10m, bucketId = SystemCatalog.BucketId("other-savings") },
                new { target = "ExpensePool", mode = "Remainder", value = 0m },
                new { target = "Category", mode = "FixedAmount", value = 150m, categoryId = Restaurants },
            },
        })).EnsureSuccessStatusCode();

        async Task Add(object body) => await api.CreateAsync("/api/transactions", body);
        await Add(new { type = "Income", occurredOn = "2031-03-25", amount = 2500m, accountId = bank, categoryId = Salary });
        await Add(new { type = "Expense", occurredOn = "2031-03-01", amount = 700m, accountId = bank, categoryId = Rent });
        await Add(new { type = "Expense", occurredOn = "2031-03-12", amount = 183.40m, accountId = bank, categoryId = Restaurants });
        await Add(new { type = "InvestmentContribution", occurredOn = "2031-03-26", amount = 625m, accountId = bank, bucketId = SystemCatalog.BucketId("stocks-etfs") });
        await Add(new { type = "Savings", occurredOn = "2031-03-26", amount = 250m, accountId = bank, bucketId = SystemCatalog.BucketId("other-savings") });

        var report = await api.GetJsonAsync($"/api/reports/monthly/{period}");
        var current = report.GetProperty("current");
        current.GetProperty("income").GetDecimal().ShouldBe(2500m);
        current.GetProperty("fixedExpenses").GetDecimal().ShouldBe(700m);
        current.GetProperty("variableExpenses").GetDecimal().ShouldBe(183.40m);
        current.GetProperty("invested").GetDecimal().ShouldBe(625m);
        current.GetProperty("saved").GetDecimal().ShouldBe(250m);
        current.GetProperty("netBalance").GetDecimal().ShouldBe(1616.60m);
        current.GetProperty("savingsRate").GetDecimal().ShouldBe(0.35m);
        current.GetProperty("expenseBudget").GetDecimal().ShouldBe(1625m);

        var categories = await api.GetAsync<JsonElement[]>($"/api/reports/categories/{period}");
        var restaurants = categories.Single(c => c.GetProperty("key").GetString() == "restaurants");
        restaurants.GetProperty("budget").GetDecimal().ShouldBe(150m);
        restaurants.GetProperty("status").GetString().ShouldBe("over");

        var annual = await api.GetJsonAsync("/api/reports/annual/2031");
        annual.GetProperty("totals").GetProperty("income").GetDecimal().ShouldBe(2500m);
        annual.GetProperty("months")[2].GetProperty("cumulativeInvested").GetDecimal().ShouldBe(625m);
    }

    [Fact]
    public async Task Recurring_templates_propose_and_confirmation_creates_the_transaction()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Subscriptions card", kind = "CreditCard", currency = "EUR", openingBalance = 0 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringId = await api.CreateAsync("/api/recurring", new
        {
            name = "Spotify", type = "Expense", amount = 10.99m, accountId = bank, frequency = "Monthly",
            startOn = today.ToString("yyyy-MM-dd"), dayOfMonth = today.Day,
            categoryId = SystemCatalog.CategoryId("subscriptions"),
        });

        var pending = await api.GetAsync<JsonElement[]>("/api/expected");
        var proposal = pending.Single(p => p.GetProperty("recurringTransactionId").GetGuid() == recurringId);
        // Nothing is booked until the user confirms.
        var before = await api.GetJsonAsync($"/api/transactions?accountId={bank}");
        before.GetProperty("total").GetInt32().ShouldBe(0);

        var confirm = await api.PostAsync($"/api/expected/{proposal.GetProperty("id").GetGuid()}/confirm",
            new { amount = 11.99m });
        confirm.EnsureSuccessStatusCode();

        var after = await api.GetJsonAsync($"/api/transactions?accountId={bank}");
        after.GetProperty("total").GetInt32().ShouldBe(1);
        var booked = after.GetProperty("items")[0];
        booked.GetProperty("amount").GetDecimal().ShouldBe(11.99m);
        booked.GetProperty("source").GetString().ShouldBe("Recurring");

        (await api.PostAsync($"/api/expected/{proposal.GetProperty("id").GetGuid()}/confirm", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Every_n_days_items_propose_from_their_anchor_and_regenerate_on_edit()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Gym card", kind = "CreditCard", currency = "EUR", openingBalance = 0 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        object Gym(int interval, int? dayOfMonth = null) => new
        {
            name = "Gym every N days", type = "Expense", amount = 25m, accountId = bank, frequency = "Daily",
            interval, dayOfMonth, startOn = today.AddDays(-14).ToString("yyyy-MM-dd"),
            categoryId = SystemCatalog.CategoryId("gym"),
        };

        (await api.PostAsync("/api/recurring", Gym(15, dayOfMonth: 1))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await api.PostAsync("/api/recurring", Gym(366))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Every 15 days from 14 days ago: one overdue occurrence and one tomorrow (within the 7-day look-ahead).
        var id = await api.CreateAsync("/api/recurring", Gym(15));
        async Task<List<(Guid Id, DateOnly DueOn)>> ExpectedAsync(string status) =>
            (await api.GetAsync<JsonElement[]>($"/api/expected?status={status}"))
            .Where(e => e.GetProperty("recurringTransactionId").GetGuid() == id)
            .Select(e => (e.GetProperty("id").GetGuid(), DateOnly.Parse(e.GetProperty("dueOn").GetString()!)))
            .ToList();
        var pending = await ExpectedAsync("Pending");
        pending.Select(p => p.DueOn).ShouldBe([today.AddDays(-14), today.AddDays(1)]);

        var listed = (await api.GetAsync<JsonElement[]>("/api/recurring")).Single(r => r.GetProperty("id").GetGuid() == id);
        listed.GetProperty("frequency").GetString().ShouldBe("Daily");
        listed.GetProperty("interval").GetInt32().ShouldBe(15);
        listed.GetProperty("dayOfMonth").ValueKind.ShouldBe(JsonValueKind.Null);
        listed.GetProperty("nextDueOn").GetString().ShouldBe(today.AddDays(1).ToString("yyyy-MM-dd"));

        (await api.PostAsync($"/api/expected/{pending[0].Id}/confirm", new { })).EnsureSuccessStatusCode();

        // Every 14 days instead: tomorrow's proposal is replaced by today's; the confirmed one stays.
        (await api.PutAsync($"/api/recurring/{id}", Gym(14))).EnsureSuccessStatusCode();

        (await ExpectedAsync("Pending")).Select(p => p.DueOn).ShouldBe([today]);
        (await ExpectedAsync("Confirmed")).ShouldBe([pending[0]]);
        (await api.GetAsync<JsonElement[]>("/api/recurring")).Single(r => r.GetProperty("id").GetGuid() == id)
            .GetProperty("nextDueOn").GetString().ShouldBe(today.ToString("yyyy-MM-dd"));

        await api.PostAsync($"/api/recurring/{id}/pause", new { });
    }

    [Fact]
    public async Task Goals_track_linked_savings()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Goal bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var goal = await api.CreateAsync("/api/goals", new { name = "Emergency Fund", targetAmount = 10_000m, startingAmount = 6_000m });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Savings", occurredOn = "2026-09-01", amount = 500m, accountId = bank,
            bucketId = SystemCatalog.BucketId("emergency-fund"), goalId = goal,
        });

        var goals = await api.GetAsync<JsonElement[]>("/api/goals");
        var emergency = goals.Single(g => g.GetProperty("id").GetGuid() == goal);
        emergency.GetProperty("currentAmount").GetDecimal().ShouldBe(6_500m);
        emergency.GetProperty("progress").GetDecimal().ShouldBe(0.65m);
    }
}
