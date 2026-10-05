using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ai.Application;
using Finance.Domain;
using Integration.Tests.Infrastructure;
using Integrations.Application;
using Integrations.Application.Connections;
using Integrations.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Notifications;

namespace Integration.Tests;

/// <summary>
/// The notification centre (GET /api/notifications): every kind is computed from existing data, appears while the
/// situation needs the owner and disappears once it is resolved. The database is shared with other tests, so each
/// test only looks at the items it caused.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<JsonElement[]> ItemsAsync(ApiClient api)
    {
        var body = await api.GetJsonAsync("/api/notifications");
        body.GetProperty("partial").GetBoolean().ShouldBeFalse();
        return body.GetProperty("items").EnumerateArray().ToArray();
    }

    private static JsonElement? Find(JsonElement[] items, string id) =>
        items.Where(i => i.GetProperty("id").GetString() == id).Cast<JsonElement?>().SingleOrDefault();

    private static string Arg(JsonElement item, string name) => item.GetProperty("args").GetProperty(name).ToString();

    [Fact]
    public async Task Requires_a_full_owner_session()
    {
        var anonymous = factory.NewClient();
        (await anonymous.Http.GetAsync("/api/notifications")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recurring_items_appear_when_due_with_confirm_and_skip_and_go_once_resolved()
    {
        var api = await factory.OwnerAsync();
        var card = await api.CreateAsync("/api/accounts",
            new { name = "Notify card", kind = "CreditCard", currency = "EUR", openingBalance = 0 });
        async Task<Guid> Recurring(string name, DateOnly start) => await api.CreateAsync("/api/recurring", new
        {
            name, type = "Expense", amount = 9.99m, accountId = card, frequency = "Monthly", startOn = Iso(start),
            dayOfMonth = start.Day, categoryId = SystemCatalog.CategoryId("subscriptions"),
        });
        var dueToday = await Recurring("Notify due today", Today);
        var overdue = await Recurring("Notify overdue", Today.AddDays(-3));
        var upcoming = await Recurring("Notify upcoming", Today.AddDays(3));

        var expected = await api.GetAsync<JsonElement[]>("/api/expected");
        Guid ExpectedOf(Guid recurring) => expected
            .Single(e => e.GetProperty("recurringTransactionId").GetGuid() == recurring).GetProperty("id").GetGuid();
        var todayId = ExpectedOf(dueToday);
        var overdueId = ExpectedOf(overdue);
        var upcomingId = ExpectedOf(upcoming); // proposed a week ahead, but not due yet

        var items = await ItemsAsync(api);
        var today = Find(items, $"recurring:{todayId}").ShouldNotBeNull();
        today.GetProperty("kind").GetString().ShouldBe("RecurringDue");
        today.GetProperty("severity").GetString().ShouldBe("Info");
        today.GetProperty("targetId").GetGuid().ShouldBe(todayId);
        today.GetProperty("link").GetString().ShouldBe("/recurring");
        today.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["confirm", "skip"]);
        Arg(today, "name").ShouldBe("Notify due today");
        today.GetProperty("args").GetProperty("amount").GetDecimal().ShouldBe(9.99m);
        var late = Find(items, $"recurring:{overdueId}").ShouldNotBeNull();
        late.GetProperty("severity").GetString().ShouldBe("Warning");
        late.GetProperty("args").GetProperty("overdue").GetBoolean().ShouldBeTrue();
        Find(items, $"recurring:{upcomingId}").ShouldBeNull();
        // Warnings sort before information.
        Array.IndexOf(items, late).ShouldBeLessThan(Array.IndexOf(items, today));

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/expected/{todayId}/confirm", new { }));
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/expected/{overdueId}/skip", new { }));

        items = await ItemsAsync(api);
        Find(items, $"recurring:{todayId}").ShouldBeNull();
        Find(items, $"recurring:{overdueId}").ShouldBeNull();
    }

    [Fact]
    public async Task Closed_interest_months_ask_to_be_reconciled_until_they_are()
    {
        var api = await factory.OwnerAsync();
        var current = new DateOnly(Today.Year, Today.Month, 1);
        var start = current.AddMonths(-2);
        var savings = await api.CreateAsync("/api/accounts", new
        {
            name = "Notify Savings", kind = "Savings", currency = "EUR", openingBalance = 20_000m,
            openingBalanceOn = Iso(start), interestPayout = "Monthly",
        });
        await api.CreateAsync($"/api/accounts/{savings}/interest-rates",
            new { annualRatePercent = 3m, effectiveFrom = Iso(start) });

        var mine = (await ItemsAsync(api)).Where(i => i.GetProperty("kind").GetString() == "InterestToReconcile" &&
                                                     Arg(i, "accountId") == savings.ToString()).ToList();
        // The two closed months; the open month is not asked about yet.
        mine.Select(i => Arg(i, "month")).ShouldBe([$"{start:yyyy-MM}", $"{start.AddMonths(1):yyyy-MM}"]);
        mine.ShouldAllBe(i => i.GetProperty("link").GetString() == "/accounts");
        mine[0].GetProperty("args").GetProperty("amount").GetDecimal().ShouldBeGreaterThan(0m);
        mine[0].GetProperty("actions").EnumerateArray().Single().GetString().ShouldBe("confirm");

        var first = mine[0].GetProperty("targetId").GetGuid();
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/interest/{first}/reconcile", new { }));

        var after = await ItemsAsync(api);
        Find(after, $"interest:{first}").ShouldBeNull();
        Find(after, mine[1].GetProperty("id").GetString()!).ShouldNotBeNull();
    }

    [Fact]
    public async Task Category_limits_warn_near_and_over_this_month_and_stop_once_the_budget_allows_it()
    {
        var api = await factory.OwnerAsync();
        var period = $"{Today:yyyy-MM}";
        var gifts = SystemCatalog.CategoryId("gifts");
        var education = SystemCatalog.CategoryId("education");
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Notify budget bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        (await api.GetAsync<JsonElement[]>("/api/budgets"))
            .Any(b => b.GetProperty("effectiveFrom").GetString() == period)
            .ShouldBeFalse("this test owns the current month's budget version");

        async Task Spend(Guid category, decimal amount) => await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = Iso(Today), amount, accountId = bank, categoryId = category,
        });
        async Task Limits(decimal giftsLimit, decimal educationLimit) => await ApiClient.EnsureAsync(
            await api.PutAsync($"/api/budgets/{period}", new
            {
                note = (string?)null,
                items = new object[]
                {
                    new { target = "Category", mode = "FixedAmount", value = giftsLimit, categoryId = gifts },
                    new { target = "Category", mode = "FixedAmount", value = educationLimit, categoryId = education },
                },
            }));

        try
        {
            await Spend(gifts, 5_000m);
            await Spend(education, 4_600m);
            await Limits(4_000m, 5_000m);

            var items = await ItemsAsync(api);
            var over = Find(items, $"budget:{period}:{gifts}:over").ShouldNotBeNull();
            over.GetProperty("kind").GetString().ShouldBe("BudgetOver");
            over.GetProperty("severity").GetString().ShouldBe("Warning");
            over.GetProperty("link").GetString().ShouldBe("/budgets");
            Arg(over, "categoryKey").ShouldBe("gifts");
            over.GetProperty("args").GetProperty("budget").GetDecimal().ShouldBe(4_000m);
            over.GetProperty("args").GetProperty("spent").GetDecimal().ShouldBeGreaterThanOrEqualTo(5_000m);
            var near = Find(items, $"budget:{period}:{education}:near").ShouldNotBeNull();
            near.GetProperty("kind").GetString().ShouldBe("BudgetNear");

            // Crossing the limit is a new item (new id); a limit that allows the spending ends both.
            await Spend(education, 1_000m);
            items = await ItemsAsync(api);
            Find(items, $"budget:{period}:{education}:near").ShouldBeNull();
            Find(items, $"budget:{period}:{education}:over").ShouldNotBeNull();

            await Limits(1_000_000m, 1_000_000m);
            items = await ItemsAsync(api);
            items.ShouldNotContain(i => i.GetProperty("id").GetString()!.StartsWith($"budget:{period}:{gifts}"));
            items.ShouldNotContain(i => i.GetProperty("id").GetString()!.StartsWith($"budget:{period}:{education}"));
        }
        finally
        {
            await api.Http.DeleteAsync($"/api/budgets/{period}");
        }
    }

    [Fact]
    public async Task Reached_goals_show_until_archived()
    {
        var api = await factory.OwnerAsync();
        var reached = await api.CreateAsync("/api/goals",
            new { name = "Notify reached", targetAmount = 500m, startingAmount = 500m });
        var open = await api.CreateAsync("/api/goals",
            new { name = "Notify open", targetAmount = 500m, startingAmount = 10m });

        var items = await ItemsAsync(api);
        var item = Find(items, $"goal:{reached}").ShouldNotBeNull();
        item.GetProperty("kind").GetString().ShouldBe("GoalReached");
        Arg(item, "name").ShouldBe("Notify reached");
        Find(items, $"goal:{open}").ShouldBeNull();

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/goals/{reached}/archive", new { }));
        Find(await ItemsAsync(api), $"goal:{reached}").ShouldBeNull();
        await api.PostAsync($"/api/goals/{open}/archive", new { });
    }

    [Fact]
    public async Task Broker_connections_needing_attention_show_the_stored_error_but_never_credentials()
    {
        var api = await factory.OwnerAsync();
        const string Secret = "notify-secret-flex-token-123";
        var account = await api.CreateAsync("/api/accounts",
            new { name = "Notify broker account", kind = "Bank", currency = "EUR", openingBalance = 0 }); // any account: not synced
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIntegrationsDb>();
            var protector = scope.ServiceProvider.GetRequiredService<ICredentialProtector>();
            // Inserted directly (no sync is queued), so its status stays what the test sets.
            var connection = BrokerConnection.Create(BrokerKind.InteractiveBrokers, "zz Notify IBKR", account,
                protector.Protect(new Dictionary<string, string> { ["token"] = Secret, ["queryId"] = "1" }), null);
            connection.NeedsAttention(
                "IBKR temporarily blocked the Flex token after too many failed attempts (Flex error 1025).");
            db.Connections.Add(connection);
            await db.SaveChangesAsync();
            id = connection.Id;
        }

        try
        {
            var raw = await api.Http.GetStringAsync("/api/notifications");
            raw.ShouldNotContain(Secret);
            raw.ShouldNotContain("protectedCredentials", Case.Insensitive);
            var item = (await ItemsAsync(api)).Single(i => i.GetProperty("targetId").ToString() == id.ToString());
            item.GetProperty("kind").GetString().ShouldBe("BrokerAttention");
            item.GetProperty("severity").GetString().ShouldBe("Error");
            item.GetProperty("link").GetString().ShouldBe("/connections");
            Arg(item, "reason").ShouldBe("needsAttention");
            Arg(item, "broker").ShouldBe("InteractiveBrokers");
            Arg(item, "error").ShouldContain("1025");
            var firstId = item.GetProperty("id").GetString();

            // A temporary failure on an active connection is a warning with a different id (new again).
            await Mutate(id, c => { c.SetEnabled(true); c.TemporaryFailure("IBKR returned 503."); });
            item = (await ItemsAsync(api)).Single(i => i.GetProperty("targetId").ToString() == id.ToString());
            item.GetProperty("severity").GetString().ShouldBe("Warning");
            Arg(item, "reason").ShouldBe("syncFailed");
            item.GetProperty("id").GetString().ShouldNotBe(firstId);

            // A successful sync resolves it; a disabled connection stays quiet whatever its last error.
            await Mutate(id, c => c.Succeeded(DateTimeOffset.UtcNow));
            (await ItemsAsync(api)).ShouldNotContain(i => i.GetProperty("targetId").ToString() == id.ToString());
            await Mutate(id, c => { c.NeedsAttention("Token rejected."); c.SetEnabled(false); });
            (await ItemsAsync(api)).ShouldNotContain(i => i.GetProperty("targetId").ToString() == id.ToString());
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IIntegrationsDb>();
            await db.Connections.Where(c => c.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Ai_writes_of_the_last_24_hours_are_summarised_per_client()
    {
        var owner = await factory.OwnerAsync();
        var bank = await owner.CreateAsync("/api/accounts",
            new { name = "Notify AI bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var response = await owner.PostAsync("/api/ai-admin/clients",
            new { name = "Notify AI writer", scopes = new[] { "transactions.write" }, rateLimitPerMinute = 600 });
        await ApiClient.EnsureAsync(response);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        async Task<JsonElement> Call(string tool, object args)
        {
            using var http = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/tools/{tool}")
            {
                Content = JsonContent.Create(args),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var result = await http.SendAsync(request);
            await ApiClient.EnsureAsync(result);
            return await result.Content.ReadFromJsonAsync<JsonElement>();
        }

        var created = await Call("create_transaction", new
        {
            type = "expense", date = Iso(Today), amount = 3.5m, account_id = bank, category = "groceries",
            description = "Notify AI coffee",
        });
        await Call("delete_transaction",
            new { transaction_id = created.GetProperty("data").GetProperty("id").GetGuid() });

        var item = (await ItemsAsync(owner)).Single(i => i.GetProperty("kind").GetString() == "AiWrites" &&
                                                         Arg(i, "client") == "Notify AI writer");
        item.GetProperty("args").GetProperty("count").GetInt32().ShouldBe(2);
        item.GetProperty("args").GetProperty("deleted").GetInt32().ShouldBe(1);
        item.GetProperty("link").GetString().ShouldBe("/ai");
        item.ToString().ShouldNotContain(token);

        // A day later the same writes are old news.
        await using var scope = factory.Services.CreateAsyncScope();
        var later = new AiNotificationSource(scope.ServiceProvider.GetRequiredService<IAiDb>(),
            new ShiftedClock(TimeSpan.FromHours(25)));
        (await later.GetAsync(Today.AddDays(1), CancellationToken.None))
            .ShouldNotContain(i => (string?)i.Args["client"] == "Notify AI writer");
    }

    private async Task Mutate(Guid id, Action<BrokerConnection> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IIntegrationsDb>();
        var connection = await db.Connections.SingleAsync(c => c.Id == id);
        change(connection);
        await db.SaveChangesAsync();
    }

    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset;
    }
}
