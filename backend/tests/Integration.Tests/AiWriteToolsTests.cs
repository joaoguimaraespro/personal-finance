extern alias mcp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ai.Application.Gateway;
using Finance.Application.Abstractions;
using Finance.Domain;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using SharedKernel;

namespace Integration.Tests;

/// <summary>
/// AI write access (ADR-0008): every write tool needs its opt-in write scope, validates strictly, refuses
/// broker-sourced, investment, estimated-interest and archived records, spends a separate write budget, honours
/// idempotency keys, is audited as the AI client with a before/after summary, and deletes into a 30-day recycle bin.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AiWriteToolsTests(ApiFactory factory)
{
    private const string Period = "2041-04";
    private const string Day = "2041-04-10";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<string> TokenAsync(ApiClient owner, string name, string[] scopes, int? writesPerHour = null)
    {
        var response = await owner.PostAsync("/api/ai-admin/clients",
            new { name, scopes, rateLimitPerMinute = 600, writesPerHour });
        await ApiClient.EnsureAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> CallAsync(string token, string tool,
        object? args = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/tools/{tool}")
        {
            Content = JsonContent.Create(args ?? new { }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonSerializer.Deserialize<JsonElement>(raw), raw);
    }

    private static Task<Guid> BankAsync(ApiClient owner, string name) =>
        owner.CreateAsync("/api/accounts", new { name, kind = "Bank", currency = "EUR", openingBalance = 0, institution = "AI write bank" });

    private static Guid IdOf(JsonElement body) => body.GetProperty("data").GetProperty("id").GetGuid();

    private static async Task<int> CountOnAccountAsync(ApiClient owner, Guid account) =>
        (await owner.GetJsonAsync($"/api/transactions?accountId={account}&pageSize=200")).GetProperty("total").GetInt32();

    private async Task<JsonElement[]> AuditForAsync(ApiClient owner, string clientName) =>
        (await owner.GetAsync<JsonElement[]>("/api/ai-admin/audit?limit=500"))
        .Where(e => e.GetProperty("clientName").GetString() == clientName).ToArray();

    [Fact]
    public async Task Writes_need_a_write_scope_and_ids_are_shown_only_to_writers()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI scopes bank");
        await owner.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = Day, amount = 7.5m, accountId = bank,
            categoryId = SystemCatalog.CategoryId("groceries"), description = "Scope probe",
        });
        var reader = await TokenAsync(owner, "AI reader only", ["transactions.read", "accounts.balances.read"]);
        var writer = await TokenAsync(owner, "AI scoped writer", ["transactions.read", "accounts.balances.read", "transactions.write"]);

        var (status, _, _) = await CallAsync(reader, "create_transaction",
            new { type = "expense", date = Day, amount = 1m, account_id = bank, category = "groceries" });
        status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(reader, "delete_transaction", new { transaction_id = Guid.NewGuid() })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await AuditForAsync(owner, "AI reader only")).ShouldContain(e =>
            e.GetProperty("tool").GetString() == "create_transaction" && e.GetProperty("decision").GetString() == "Denied" &&
            e.GetProperty("write").GetBoolean());
        (await CountOnAccountAsync(owner, bank)).ShouldBe(1);

        // Read answers are unchanged for read-only clients: no ids anywhere.
        var (_, readBody, readRaw) = await CallAsync(reader, "get_transactions", new { period = Period, search = "Scope probe" });
        readBody.GetProperty("data").GetProperty("items").EnumerateArray().ShouldHaveSingleItem().TryGetProperty("id", out _).ShouldBeFalse();
        readRaw.ShouldNotContain(bank.ToString());
        var (_, readAccounts, _) = await CallAsync(reader, "get_accounts");
        readAccounts.GetProperty("data").GetProperty("items").EnumerateArray().Where(HasId).ShouldBeEmpty();

        // A writer sees the ids it needs: editable rows and accounts that accept entries.
        var (_, writeBody, _) = await CallAsync(writer, "get_transactions", new { period = Period, search = "Scope probe" });
        writeBody.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        var (_, writeAccounts, _) = await CallAsync(writer, "get_accounts");
        writeAccounts.GetProperty("data").GetProperty("items").EnumerateArray().Where(HasId)
            .Select(i => i.GetProperty("id").GetGuid()).ShouldContain(bank);
    }

    [Fact]
    public async Task Create_update_delete_restore_and_purge_a_transaction_as_the_ai_client()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI crud bank");
        var token = await TokenAsync(owner, "Claude writer", ["transactions.write"]);

        var (status, created, raw) = await CallAsync(token, "create_transaction", new
        {
            type = "expense", date = Day, amount = 12.34m, account_id = bank, category = "groceries", description = "Lunch",
        });
        status.ShouldBe(HttpStatusCode.OK, raw);
        var id = IdOf(created);
        created.GetProperty("data").GetProperty("summary").GetString()!.ShouldContain("12.34 EUR");
        created.GetProperty("data").GetProperty("details").GetProperty("description").GetProperty("untrusted_text").GetString().ShouldBe("Lunch");
        created.GetProperty("notice").GetString()!.ShouldContain("untrusted_text");

        var row = await owner.GetJsonAsync($"/api/transactions/{id}");
        row.GetProperty("amount").GetDecimal().ShouldBe(12.34m);
        row.GetProperty("source").GetString().ShouldBe("Manual");
        row.GetProperty("categoryKey").GetString().ShouldBe("groceries");

        var (updateStatus, _, updateRaw) = await CallAsync(token, "update_transaction",
            new { transaction_id = id, amount = 15m, category = "restaurants" });
        updateStatus.ShouldBe(HttpStatusCode.OK, updateRaw);
        row = await owner.GetJsonAsync($"/api/transactions/{id}");
        row.GetProperty("amount").GetDecimal().ShouldBe(15m);
        row.GetProperty("categoryKey").GetString().ShouldBe("restaurants");
        row.GetProperty("description").GetString().ShouldBe("Lunch");

        // The ledger's own audit trail names the AI client as the actor.
        var history = await owner.GetAsync<JsonElement[]>($"/api/transactions/{id}/history");
        history.Select(h => (h.GetProperty("action").GetString(), h.GetProperty("actor").GetString()))
            .ShouldBe([("Created", "ai:Claude writer"), ("Updated", "ai:Claude writer")]);

        // The gateway's audit marks writes, links the record and keeps a before/after summary.
        var update = (await AuditForAsync(owner, "Claude writer")).First(e => e.GetProperty("tool").GetString() == "update_transaction");
        update.GetProperty("write").GetBoolean().ShouldBeTrue();
        update.GetProperty("recordId").GetGuid().ShouldBe(id);
        var changes = JsonDocument.Parse(update.GetProperty("changes").GetString()!).RootElement;
        changes.GetProperty("before").GetProperty("amount").GetDecimal().ShouldBe(12.34m);
        changes.GetProperty("after").GetProperty("amount").GetDecimal().ShouldBe(15m);
        changes.GetProperty("after").GetProperty("category").GetString().ShouldBe("Restaurants");

        // Delete is soft: the row disappears from the app and lands in the recycle bin.
        var (deleteStatus, deleted, _) = await CallAsync(token, "delete_transaction", new { transaction_id = id });
        deleteStatus.ShouldBe(HttpStatusCode.OK);
        deleted.GetProperty("data").GetProperty("summary").GetString()!.ShouldContain("recycle bin");
        (await owner.Http.GetAsync($"/api/transactions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bin = await owner.GetAsync<JsonElement[]>("/api/ai-admin/recycle-bin");
        var item = bin.Single(b => b.GetProperty("recordId").GetGuid() == id);
        item.GetProperty("clientName").GetString().ShouldBe("Claude writer");
        item.GetProperty("summary").GetString()!.ShouldContain("Lunch");
        var deletedAt = item.GetProperty("deletedAtUtc").GetDateTimeOffset();
        item.GetProperty("purgeAfterUtc").GetDateTimeOffset().ShouldBe(deletedAt.AddDays(30), TimeSpan.FromSeconds(1));
        (await AuditForAsync(owner, "Claude writer")).First(e => e.GetProperty("tool").GetString() == "delete_transaction")
            .GetProperty("recordId").GetGuid().ShouldBe(id);

        // One click restores it.
        var restore = await owner.PostAsync($"/api/ai-admin/recycle-bin/{item.GetProperty("id").GetGuid()}/restore", new { });
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetJsonAsync($"/api/transactions/{id}")).GetProperty("amount").GetDecimal().ShouldBe(15m);
        (await owner.GetAsync<JsonElement[]>("/api/ai-admin/recycle-bin")).ShouldNotContain(b => b.GetProperty("recordId").GetGuid() == id);
        (await owner.PostAsync($"/api/ai-admin/recycle-bin/{item.GetProperty("id").GetGuid()}/restore", new { })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Deleted again and left alone: kept for 30 days, then purged for good (a fake clock moves time forward).
        (await CallAsync(token, "delete_transaction", new { transaction_id = id })).Status.ShouldBe(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var recycle = scope.ServiceProvider.GetRequiredService<AiRecycleBin>();
            await recycle.PurgeAsync(DateTimeOffset.UtcNow.AddDays(29), CancellationToken.None);
            (await recycle.ListAsync(CancellationToken.None)).ShouldContain(b => b.RecordId == id);
            await recycle.PurgeAsync(DateTimeOffset.UtcNow.AddDays(31), CancellationToken.None);
            (await recycle.ListAsync(CancellationToken.None)).ShouldNotContain(b => b.RecordId == id);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            (await finance.Transactions.IgnoreQueryFilters().AnyAsync(t => t.Id == id)).ShouldBeFalse();
            (await finance.TransactionAudits.Where(a => a.TransactionId == id).Select(a => a.Action).ToListAsync())
                .ShouldContain(AuditAction.Restored);
        }
    }

    [Fact]
    public async Task Invalid_arguments_are_refused_before_anything_is_written()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI invalid bank");
        var token = await TokenAsync(owner, "AI invalid writer", ["transactions.write"]);
        object Expense(object overrides) => Merge(new Dictionary<string, object?>
        {
            ["type"] = "expense", ["date"] = Day, ["amount"] = 10m, ["account_id"] = bank, ["category"] = "groceries",
        }, overrides);

        var cases = new (object Args, HttpStatusCode Status, string Message)[]
        {
            (Expense(new { amount = -5m }), HttpStatusCode.BadRequest, "amount must be between"),
            (Expense(new { amount = 1.234m }), HttpStatusCode.BadRequest, "at most 2 decimal"),
            (Expense(new { amount = "ten" }), HttpStatusCode.BadRequest, "amount must be a number"),
            (Expense(new { date = "2041-13-01" }), HttpStatusCode.BadRequest, "date must be a date"),
            (Expense(new { category = "nonsense category" }), HttpStatusCode.BadRequest, "Unknown expense category"),
            (Expense(new { category = (string?)null }), HttpStatusCode.BadRequest, "category is required"),
            (Expense(new { sql = "drop table" }), HttpStatusCode.BadRequest, "Unknown argument"),
            (Expense(new { description = "Pay ‮ecnalab" }), HttpStatusCode.BadRequest, "invisible characters"),
            (Expense(new { description = new string('x', 121) }), HttpStatusCode.BadRequest, "1-120 characters"),
            (Expense(new { account_id = "not-an-id" }), HttpStatusCode.BadRequest, "must be an id"),
            (Expense(new { account_id = Guid.NewGuid() }), HttpStatusCode.NotFound, "account not found"),
            (Expense(new { type = "investment" }), HttpStatusCode.BadRequest, "type must be one of"),
            (Expense(new { type = "transfer", category = (string?)null }), HttpStatusCode.BadRequest, "to_account_id is required"),
            (new { type = "expense", amount = 10m }, HttpStatusCode.BadRequest, "date is required"),
            (Expense(new { idempotency_key = "short" }), HttpStatusCode.BadRequest, "idempotency_key must be"),
        };

        foreach (var (args, expected, message) in cases)
        {
            var (status, body, raw) = await CallAsync(token, "create_transaction", args);
            status.ShouldBe(expected, raw);
            body.GetProperty("error").GetString()!.ShouldContain(message, Case.Insensitive, raw);
        }

        (await CallAsync(token, "update_transaction", new { transaction_id = Guid.NewGuid(), amount = 3m })).Status
            .ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(token, "update_transaction", new { transaction_id = Guid.NewGuid() })).Status
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CountOnAccountAsync(owner, bank)).ShouldBe(0);
        (await AuditForAsync(owner, "AI invalid writer")).ShouldAllBe(e => e.GetProperty("decision").GetString() != "Allowed");
    }

    [Fact]
    public async Task Broker_sourced_investment_estimated_and_archived_records_are_refused()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI refusal bank");
        var token = await TokenAsync(owner, "AI refused writer", ["transactions.write"]);
        Guid broker, investment, estimate, brokerAccount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            var day = DateOnly.Parse(Day, System.Globalization.CultureInfo.InvariantCulture);
            Transaction Make(TransactionDraft draft, DataSource source, string? external = null) =>
                Transaction.Create(draft, source, externalId: external).Value;
            var t212 = Make(new TransactionDraft(TransactionType.Expense, day, 9m, "EUR", bank,
                SystemCatalog.CategoryId("groceries")), DataSource.Trading212, "ai-write-test-" + Guid.NewGuid());
            var savings = Make(new TransactionDraft(TransactionType.Savings, day, 50m, "EUR", bank,
                BucketId: SystemCatalog.BucketId("travel-fund")), DataSource.Manual);
            var interest = Make(new TransactionDraft(TransactionType.Income, day, 1.2m, "EUR", bank,
                SystemCatalog.CategoryId("interest")), DataSource.InterestEstimate);
            var account = Account.Create("AI broker account", AccountKind.Broker, "EUR", 0, day, "Trading212");
            finance.Transactions.AddRange(t212, savings, interest);
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync();
            (broker, investment, estimate, brokerAccount) = (t212.Id, savings.Id, interest.Id, account.Id);
        }

        foreach (var (id, message) in new[] { (broker, "broker"), (investment, "savings"), (estimate, "Estimated interest") })
        {
            var (status, body, _) = await CallAsync(token, "update_transaction", new { transaction_id = id, amount = 1m });
            status.ShouldBe(HttpStatusCode.Forbidden);
            body.GetProperty("error").GetString()!.ShouldContain(message, Case.Insensitive);
            (await CallAsync(token, "delete_transaction", new { transaction_id = id })).Status.ShouldBe(HttpStatusCode.Forbidden);
        }

        var (brokerStatus, brokerBody, _) = await CallAsync(token, "create_transaction",
            new { type = "expense", date = Day, amount = 3m, account_id = brokerAccount, category = "groceries" });
        brokerStatus.ShouldBe(HttpStatusCode.Forbidden);
        brokerBody.GetProperty("error").GetString()!.ShouldContain("read-only");

        // Archived accounts accept nothing, and their rows cannot be changed.
        var old = await BankAsync(owner, "AI archived bank");
        var oldRow = await owner.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = Day, amount = 4m, accountId = old, categoryId = SystemCatalog.CategoryId("groceries"),
        });
        await ApiClient.EnsureAsync(await owner.PostAsync($"/api/accounts/{old}/archive", new { }));
        (await CallAsync(token, "update_transaction", new { transaction_id = oldRow, amount = 5m })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(token, "create_transaction",
            new { type = "expense", date = Day, amount = 3m, account_id = old, category = "groceries" })).Status.ShouldBe(HttpStatusCode.Forbidden);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            (await finance.Transactions.Where(t => t.Id == broker || t.Id == investment || t.Id == estimate)
                .Select(t => t.OriginalAmount).ToListAsync()).ShouldBe([9m, 50m, 1.2m], ignoreOrder: true);
        }

        (await AuditForAsync(owner, "AI refused writer")).Where(e => e.GetProperty("tool").GetString() == "update_transaction")
            .ShouldAllBe(e => e.GetProperty("decision").GetString() == "Denied");
    }

    [Fact]
    public async Task Writes_have_their_own_low_hourly_budget()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI budget bank");
        var token = await TokenAsync(owner, "AI two writes", ["transactions.write", "transactions.read"], writesPerHour: 2);
        object Args(decimal amount) => new { type = "income", date = Day, amount, account_id = bank, category = "salary" };

        (await CallAsync(token, "create_transaction", Args(1m))).Status.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(token, "create_transaction", Args(2m))).Status.ShouldBe(HttpStatusCode.OK);
        var (status, body, _) = await CallAsync(token, "create_transaction", Args(3m));

        status.ShouldBe(HttpStatusCode.TooManyRequests);
        body.GetProperty("error").GetString()!.ShouldContain("2 per hour");
        (await CallAsync(token, "get_transactions", new { period = Period })).Status.ShouldBe(HttpStatusCode.OK); // reads unaffected
        (await CountOnAccountAsync(owner, bank)).ShouldBe(2);
        var clients = await owner.GetAsync<JsonElement[]>("/api/ai-admin/clients");
        var client = clients.Single(c => c.GetProperty("name").GetString() == "AI two writes");
        client.GetProperty("writesPerHour").GetInt32().ShouldBe(2);
        client.GetProperty("writesLast24h").GetInt32().ShouldBe(2);
        (await AuditForAsync(owner, "AI two writes")).ShouldContain(e => e.GetProperty("decision").GetString() == "RateLimited");

        // The owner can raise the budget; new clients default to 20 writes per hour.
        await ApiClient.EnsureAsync(await owner.PutAsync($"/api/ai-admin/clients/{client.GetProperty("id").GetGuid()}",
            new { scopes = new[] { "transactions.write" }, writesPerHour = 5 }));
        (await CallAsync(token, "create_transaction", Args(3m))).Status.ShouldBe(HttpStatusCode.OK);
        await TokenAsync(owner, "AI default budget", ["transactions.write"]);
        (await owner.GetAsync<JsonElement[]>("/api/ai-admin/clients")).Single(c => c.GetProperty("name").GetString() == "AI default budget")
            .GetProperty("writesPerHour").GetInt32().ShouldBe(20);
        (await owner.PutAsync($"/api/ai-admin/clients/{client.GetProperty("id").GetGuid()}",
            new { scopes = new[] { "transactions.write" }, writesPerHour = 1000 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_idempotency_key_makes_a_retried_write_a_no_op()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI idempotent bank");
        var token = await TokenAsync(owner, "AI retrying writer", ["transactions.write"]);
        var args = new { type = "expense", date = Day, amount = 20m, account_id = bank, category = "groceries", idempotency_key = "retry-0001-abc" };

        var (first, firstBody, _) = await CallAsync(token, "create_transaction", args);
        var (second, secondBody, _) = await CallAsync(token, "create_transaction", args);

        first.ShouldBe(HttpStatusCode.OK);
        second.ShouldBe(HttpStatusCode.OK);
        IdOf(secondBody).ShouldBe(IdOf(firstBody));
        secondBody.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        (await CountOnAccountAsync(owner, bank)).ShouldBe(1);

        // The same key for a different change is refused rather than silently ignored.
        var (conflict, conflictBody, _) = await CallAsync(token, "create_transaction", args with { amount = 21m });
        conflict.ShouldBe(HttpStatusCode.Conflict);
        conflictBody.GetProperty("error").GetString()!.ShouldContain("idempotency_key");
        (await CountOnAccountAsync(owner, bank)).ShouldBe(1);

        // Keys are per client: another client's identical key is a separate change.
        var other = await TokenAsync(owner, "AI other retrying writer", ["transactions.write"]);
        (await CallAsync(other, "create_transaction", args)).Status.ShouldBe(HttpStatusCode.OK);
        (await CountOnAccountAsync(owner, bank)).ShouldBe(2);
    }

    [Fact]
    public async Task Recurring_items_can_be_created_confirmed_skipped_and_edited()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI recurring writes bank");
        var token = await TokenAsync(owner, "AI recurring writer", ["recurring.read", "recurring.write"]);
        var today = Today.ToString("yyyy-MM-dd");
        var created = new List<Guid>();
        try
        {
            foreach (var name in new[] { "AI write gym", "AI write magazine" })
            {
                var (status, body, raw) = await CallAsync(token, "upsert_recurring", new
                {
                    name, type = "expense", amount = 30m, frequency = "monthly", account_id = bank, category = "gym", start_on = today,
                });
                status.ShouldBe(HttpStatusCode.OK, raw);
                created.Add(IdOf(body));
            }

            var (_, recurring, _) = await CallAsync(token, "get_recurring", new { days = 10 });
            var upcoming = recurring.GetProperty("data").GetProperty("upcoming").EnumerateArray().ToList();
            Guid Expected(string name) => upcoming.First(u => u.GetProperty("name").GetProperty("untrusted_text").GetString() == name &&
                                                            u.GetProperty("status").GetString() == "awaiting_confirmation")
                .GetProperty("expectedId").GetGuid();
            recurring.GetProperty("data").GetProperty("items").EnumerateArray().Where(HasId)
                .Select(i => i.GetProperty("id").GetGuid()).ShouldContain(created[0]);

            var gym = Expected("AI write gym");
            var (confirmStatus, confirmed, confirmRaw) = await CallAsync(token, "confirm_expected", new { expected_id = gym, amount = 31.5m });
            confirmStatus.ShouldBe(HttpStatusCode.OK, confirmRaw);
            var transaction = await owner.GetJsonAsync($"/api/transactions/{IdOf(confirmed)}");
            transaction.GetProperty("amount").GetDecimal().ShouldBe(31.5m);
            transaction.GetProperty("source").GetString().ShouldBe("Recurring");
            (await CallAsync(token, "confirm_expected", new { expected_id = gym })).Status.ShouldBe(HttpStatusCode.Conflict);

            var magazine = Expected("AI write magazine");
            (await CallAsync(token, "skip_expected", new { expected_id = magazine })).Status.ShouldBe(HttpStatusCode.OK);
            (await CallAsync(token, "skip_expected", new { expected_id = magazine })).Status.ShouldBe(HttpStatusCode.Conflict);
            (await owner.GetAsync<JsonElement[]>("/api/expected?status=Skipped")).ShouldContain(e => e.GetProperty("id").GetGuid() == magazine);

            var (editStatus, _, editRaw) = await CallAsync(token, "upsert_recurring", new { recurring_id = created[0], amount = 35m });
            editStatus.ShouldBe(HttpStatusCode.OK, editRaw);
            var templates = await owner.GetAsync<JsonElement[]>("/api/recurring");
            var edited = templates.Single(r => r.GetProperty("id").GetGuid() == created[0]);
            edited.GetProperty("amount").GetDecimal().ShouldBe(35m);
            edited.GetProperty("name").GetString().ShouldBe("AI write gym");

            (await CallAsync(token, "upsert_recurring", new { name = "Incomplete" })).Status.ShouldBe(HttpStatusCode.BadRequest);
            (await CallAsync(token, "upsert_recurring", new { recurring_id = Guid.NewGuid(), amount = 1m })).Status.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            foreach (var id in created)
            {
                await owner.PostAsync($"/api/recurring/{id}/pause", new { });
            }
        }
    }

    [Fact]
    public async Task Budgets_and_goals_can_be_planned()
    {
        var owner = await factory.OwnerAsync();
        var token = await TokenAsync(owner, "AI planner", ["planning.write", "goals.read"]);

        var (status, _, raw) = await CallAsync(token, "set_budget_limit", new { category = "restaurants", amount = 150m, from_period = "2047-05" });
        status.ShouldBe(HttpStatusCode.OK, raw);
        var budget = await owner.GetJsonAsync("/api/budgets/effective/2047-05");
        budget.GetProperty("items").EnumerateArray().ShouldContain(i =>
            i.GetProperty("categoryId").GetString() == SystemCatalog.CategoryId("restaurants").ToString() &&
            i.GetProperty("mode").GetString() == "FixedAmount" && i.GetProperty("value").GetDecimal() == 150m);
        (await CallAsync(token, "set_budget_limit", new { category = "restaurants", amount = 0m, from_period = "2047-05" })).Status
            .ShouldBe(HttpStatusCode.OK);
        (await owner.GetJsonAsync("/api/budgets/effective/2047-05")).GetProperty("items").EnumerateArray()
            .ShouldNotContain(i => i.GetProperty("categoryId").GetString() == SystemCatalog.CategoryId("restaurants").ToString());
        (await CallAsync(token, "set_budget_limit", new { category = "restaurants", amount = 0m, from_period = "2047-05" })).Status
            .ShouldBe(HttpStatusCode.Conflict);
        (await CallAsync(token, "set_budget_limit", new { category = "restaurants", amount = 10m, from_period = "2020-01" })).Status
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(token, "set_budget_limit", new { category = "salary", amount = 10m })).Status
            .ShouldBe(HttpStatusCode.BadRequest); // income categories have no budget

        // Changing an existing version keeps its other items (and adds the new limit as a new item).
        await ApiClient.EnsureAsync(await owner.PutAsync("/api/budgets/2047-04", new
        {
            note = "AI planning test",
            items = new[] { new { target = "ExpensePool", mode = "PercentOfIncome", value = 0.6m, bucketId = (Guid?)null, categoryId = (Guid?)null } },
        }));
        (await CallAsync(token, "set_budget_limit", new { category = "groceries", amount = 300m, from_period = "2047-04" })).Status
            .ShouldBe(HttpStatusCode.OK);
        var april = await owner.GetJsonAsync("/api/budgets/effective/2047-04");
        april.GetProperty("note").GetString().ShouldBe("AI planning test");
        april.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("target").GetString())
            .ShouldBe(["ExpensePool", "Category"], ignoreOrder: true);

        var (createStatus, goalBody, createRaw) = await CallAsync(token, "upsert_goal", new { name = "AI trip", target_amount = 2000m, target_date = "2048-06-30" });
        createStatus.ShouldBe(HttpStatusCode.OK, createRaw);
        var goal = IdOf(goalBody);
        var (_, goals, _) = await CallAsync(token, "get_goals");
        goals.GetProperty("data").GetProperty("items").EnumerateArray().ShouldContain(g => g.GetProperty("id").GetGuid() == goal);
        (await CallAsync(token, "upsert_goal", new { goal_id = goal, target_amount = 2500m })).Status.ShouldBe(HttpStatusCode.OK);
        var (addStatus, added, _) = await CallAsync(token, "add_to_goal", new { goal_id = goal, amount = 100m });
        addStatus.ShouldBe(HttpStatusCode.OK);
        added.GetProperty("data").GetProperty("summary").GetString()!.ShouldContain("100.00 EUR");
        var saved = (await owner.GetAsync<JsonElement[]>("/api/goals")).Single(g => g.GetProperty("id").GetGuid() == goal);
        saved.GetProperty("name").GetString().ShouldBe("AI trip");
        saved.GetProperty("targetAmount").GetDecimal().ShouldBe(2500m);
        saved.GetProperty("currentAmount").GetDecimal().ShouldBe(100m);

        await ApiClient.EnsureAsync(await owner.PostAsync($"/api/goals/{goal}/archive", new { }));
        (await CallAsync(token, "add_to_goal", new { goal_id = goal, amount = 1m })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(token, "upsert_goal", new { goal_id = goal, name = "Renamed" })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(token, "upsert_goal", new { name = "No target" })).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Hand_entered_crypto_and_manual_assets_can_be_updated_but_not_broker_positions()
    {
        var owner = await factory.OwnerAsync();
        var token = await TokenAsync(owner, "AI holdings writer", ["holdings.write", "portfolio.positions.read", "networth.read"]);
        var holding = await owner.CreateAsync("/api/portfolio/manual", new
        {
            coinId = "BTC", symbol = "BTC", name = "BTC", quantity = 0.1m, averagePrice = 20_000m, location = "AI write wallet",
            heldSince = Today.AddDays(-10),
        });
        var asset = await owner.CreateAsync("/api/assets", new { name = "AI flat", kind = "RealEstate", value = 200_000m });
        try
        {
            var (_, positions, _) = await CallAsync(token, "get_positions", new { broker = "Manual", top = 25 });
            positions.GetProperty("data").GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("symbol").GetString() == "BTC").GetProperty("locations").EnumerateArray()
                .ShouldContain(l => l.GetProperty("holdingId").GetGuid() == holding);

            var (updateStatus, _, updateRaw) = await CallAsync(token, "update_crypto_holding", new { holding_id = holding, quantity = 0.2m });
            updateStatus.ShouldBe(HttpStatusCode.OK, updateRaw);
            var (rewardStatus, _, rewardRaw) = await CallAsync(token, "add_crypto_reward", new { holding_id = holding, quantity = 0.001m, kind = "earn" });
            rewardStatus.ShouldBe(HttpStatusCode.OK, rewardRaw);
            var manual = (await owner.GetJsonAsync("/api/portfolio/manual")).GetProperty("holdings").EnumerateArray()
                .Single(h => h.GetProperty("id").GetGuid() == holding);
            manual.GetProperty("quantity").GetDecimal().ShouldBe(0.2m);
            manual.GetProperty("averagePrice").GetDecimal().ShouldBe(20_000m);
            manual.GetProperty("rewardQuantity").GetDecimal().ShouldBe(0.001m);
            manual.GetProperty("rewards")[0].GetProperty("kind").GetString().ShouldBe("Earn");

            (await CallAsync(token, "update_crypto_holding", new { holding_id = Guid.NewGuid(), quantity = 1m })).Status.ShouldBe(HttpStatusCode.NotFound);
            (await CallAsync(token, "update_crypto_holding", new { holding_id = holding })).Status.ShouldBe(HttpStatusCode.BadRequest);
            (await CallAsync(token, "add_crypto_reward", new { holding_id = holding, quantity = 1m, received_on = Today.AddDays(5).ToString("yyyy-MM-dd") }))
                .Status.ShouldBe(HttpStatusCode.BadRequest);

            var (_, netWorth, _) = await CallAsync(token, "get_net_worth");
            netWorth.GetProperty("data").GetProperty("manualAssets").EnumerateArray().ShouldContain(a => a.GetProperty("id").GetGuid() == asset);
            var (valueStatus, _, valueRaw) = await CallAsync(token, "update_asset_value", new { asset_id = asset, value = 210_000m });
            valueStatus.ShouldBe(HttpStatusCode.OK, valueRaw);
            (await owner.GetAsync<JsonElement[]>("/api/assets")).Single(a => a.GetProperty("id").GetGuid() == asset)
                .GetProperty("currentValue").GetDecimal().ShouldBe(210_000m);
        }
        finally
        {
            await owner.Http.DeleteAsync($"/api/portfolio/manual/{holding}");
            await owner.PostAsync($"/api/assets/{asset}/archive", new { });
        }

        (await CallAsync(token, "update_asset_value", new { asset_id = asset, value = 1m })).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mcp_lists_write_tools_only_to_clients_holding_a_write_scope()
    {
        var owner = await factory.OwnerAsync();
        var bank = await BankAsync(owner, "AI mcp write bank");
        var reader = await TokenAsync(owner, "AI MCP reader", ["accounts.balances.read"]);
        var writer = await TokenAsync(owner, "AI MCP writer", ["accounts.balances.read", "transactions.write"]);
        await using var mcpHost = new McpFactory(factory);

        async Task<McpClient> ConnectAsync(string token)
        {
            var http = mcpHost.CreateClient();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            }, http, null, false);
            return await McpClient.CreateAsync(transport);
        }

        await using (var client = await ConnectAsync(reader))
        {
            (await client.ListToolsAsync()).Select(t => t.Name).ShouldBe(["get_accounts"]);
        }

        await using (var client = await ConnectAsync(writer))
        {
            client.ServerInstructions!.ShouldContain("only write when the user explicitly asked", Case.Insensitive);
            var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name);
            tools.Keys.ShouldBe(["get_accounts", "create_transaction", "update_transaction", "delete_transaction"], ignoreOrder: true);
            tools["get_accounts"].ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true);
            foreach (var name in new[] { "create_transaction", "update_transaction", "delete_transaction" })
            {
                tools[name].ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(false);
            }

            tools["delete_transaction"].ProtocolTool.Annotations!.DestructiveHint.ShouldBe(true);
            tools["create_transaction"].ProtocolTool.Annotations!.DestructiveHint.ShouldBe(false);
            tools["create_transaction"].ProtocolTool.Annotations!.IdempotentHint.ShouldBe(false);
            tools["update_transaction"].ProtocolTool.Annotations!.IdempotentHint.ShouldBe(true);

            var result = await client.CallToolAsync("create_transaction", new Dictionary<string, object?>
            {
                ["type"] = "expense", ["date"] = Day, ["amount"] = 9.99m, ["account_id"] = bank.ToString(),
                ["category"] = "groceries", ["description"] = "Via MCP",
            });
            result.IsError.ShouldNotBe(true);
            var text = string.Join("", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
            var id = JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("id").GetGuid();
            (await owner.GetJsonAsync($"/api/transactions/{id}")).GetProperty("description").GetString().ShouldBe("Via MCP");

            var refused = await client.CallToolAsync("delete_transaction",
                new Dictionary<string, object?> { ["transaction_id"] = Guid.NewGuid().ToString() });
            refused.IsError.ShouldBe(true);
        }
    }

    private static bool HasId(JsonElement e) => e.TryGetProperty("id", out _);

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> args, object overrides)
    {
        foreach (var p in overrides.GetType().GetProperties())
        {
            var value = p.GetValue(overrides);
            if (value is null)
            {
                args.Remove(p.Name);
            }
            else
            {
                args[p.Name] = value;
            }
        }

        return args;
    }
}
