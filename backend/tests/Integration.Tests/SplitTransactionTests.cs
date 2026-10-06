using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>
/// Split transactions: one movement (one bank line, one total) spread over category lines. Every figure grouped by
/// category — reports, budgets, the list filter, exports and the AI tools — counts the lines, never the whole total
/// against one category. Months of 2049 are used only here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SplitTransactionTests(ApiFactory factory)
{
    private static readonly Guid Electricity = SystemCatalog.CategoryId("electricity"); // Fixed by default
    private static readonly Guid Groceries = SystemCatalog.CategoryId("groceries");     // Variable by default
    private static readonly Guid WaterGas = SystemCatalog.CategoryId("water-gas");
    private static readonly Guid Salary = SystemCatalog.CategoryId("salary");
    private static readonly Guid Bonus = SystemCatalog.CategoryId("bonus");

    private static object Split(Guid category, decimal amount, string? note = null) => new { categoryId = category, amount, note };

    private static object Expense(Guid account, string day, decimal amount, params object[] splits) => new
    {
        type = "Expense", occurredOn = day, amount, accountId = account, description = "Energy bill", splits,
    };

    [Fact]
    public async Task A_split_expense_is_counted_by_line_in_reports_budgets_and_the_category_filter()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Split bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        (await api.PutAsync("/api/budgets/2049-03", new
        {
            items = new object[] { new { target = "Category", mode = "FixedAmount", value = 50m, categoryId = Electricity } },
        })).EnsureSuccessStatusCode();

        var id = await api.CreateAsync("/api/transactions",
            Expense(bank, "2049-03-05", 100m, Split(Electricity, 60m, "power"), Split(Groceries, 40m)));
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2049-03-06", amount = 20m, accountId = bank, categoryId = Groceries,
        });

        var created = await api.GetJsonAsync($"/api/transactions/{id}");
        created.GetProperty("categoryId").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetProperty("nature").ValueKind.ShouldBe(JsonValueKind.Null);
        var lines = created.GetProperty("splits").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("categoryKey").GetString()).ShouldBe(["electricity", "groceries"]);
        lines.Select(l => l.GetProperty("nature").GetString()).ShouldBe(["Fixed", "Variable"]);
        lines[0].GetProperty("note").GetString().ShouldBe("power");

        // Monthly: fixed/variable by line; one transaction however many lines.
        var month = (await api.GetJsonAsync("/api/reports/monthly/2049-03")).GetProperty("current");
        month.GetProperty("fixedExpenses").GetDecimal().ShouldBe(60m);
        month.GetProperty("variableExpenses").GetDecimal().ShouldBe(60m);
        month.GetProperty("totalExpenses").GetDecimal().ShouldBe(120m);
        month.GetProperty("transactionCount").GetInt32().ShouldBe(2);

        // Categories and budget limits by line.
        var categories = await api.GetAsync<JsonElement[]>("/api/reports/categories/2049-03");
        var electricity = categories.Single(c => c.GetProperty("key").GetString() == "electricity");
        electricity.GetProperty("actual").GetDecimal().ShouldBe(60m);
        electricity.GetProperty("budget").GetDecimal().ShouldBe(50m);
        electricity.GetProperty("status").GetString().ShouldBe("over");
        categories.Single(c => c.GetProperty("key").GetString() == "groceries").GetProperty("actual").GetDecimal().ShouldBe(60m);

        // The list filtered by a category finds the split row and shows its part in that category.
        var filtered = await api.GetJsonAsync($"/api/transactions?categoryId={Electricity}&from=2049-03-01&to=2049-03-31");
        var row = filtered.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetGuid().ShouldBe(id);
        row.GetProperty("categoryAmount").GetDecimal().ShouldBe(60m);
        row.GetProperty("amount").GetDecimal().ShouldBe(100m);
        var groceries = await api.GetJsonAsync($"/api/transactions?categoryId={Groceries}&from=2049-03-01&to=2049-03-31");
        groceries.GetProperty("total").GetInt32().ShouldBe(2);
        var fixedOnly = await api.GetJsonAsync("/api/transactions?nature=Fixed&from=2049-03-01&to=2049-03-31");
        fixedOnly.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ShouldBe([id]);
    }

    [Fact]
    public async Task Editing_replaces_lines_removing_gives_one_category_and_both_are_audited()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Split edit bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var id = await api.CreateAsync("/api/transactions",
            Expense(bank, "2049-04-05", 100m, Split(Electricity, 60m), Split(WaterGas, 40m)));

        // Same total, new lines and an explicit nature for the whole movement.
        (await api.PutAsync($"/api/transactions/{id}", new
        {
            type = "Expense", occurredOn = "2049-04-05", amount = 100m, accountId = bank, nature = "Variable",
            description = "Energy bill", splits = new[] { Split(Electricity, 70m), Split(WaterGas, 30m) },
        })).EnsureSuccessStatusCode();
        var edited = await api.GetJsonAsync($"/api/transactions/{id}");
        edited.GetProperty("splits").EnumerateArray().Select(l => (l.GetProperty("amount").GetDecimal(), l.GetProperty("nature").GetString()))
            .ShouldBe([(70m, "Variable"), (30m, "Variable")]);

        // Back to a single category.
        (await api.PutAsync($"/api/transactions/{id}", new
        {
            type = "Expense", occurredOn = "2049-04-05", amount = 100m, accountId = bank, categoryId = Electricity,
            description = "Energy bill",
        })).EnsureSuccessStatusCode();
        var single = await api.GetJsonAsync($"/api/transactions/{id}");
        single.GetProperty("splits").GetArrayLength().ShouldBe(0);
        single.GetProperty("categoryKey").GetString().ShouldBe("electricity");
        single.GetProperty("nature").GetString().ShouldBe("Fixed");

        var history = await api.GetAsync<JsonElement[]>($"/api/transactions/{id}/history");
        history.Select(h => h.GetProperty("action").GetString()).ShouldBe(["Created", "Updated", "Updated"]);
        history[0].GetProperty("changes").GetString()!.ShouldContain("Splits");
        history[1].GetProperty("changes").GetString()!.ShouldContain("70.00");
        history[2].GetProperty("changes").GetString()!.ShouldContain("Splits");

        // Saving the same lines again changes nothing and adds no audit row.
        (await api.PutAsync($"/api/transactions/{id}", new
        {
            type = "Expense", occurredOn = "2049-04-05", amount = 100m, accountId = bank, categoryId = Electricity,
            description = "Energy bill",
        })).EnsureSuccessStatusCode();
        (await api.GetAsync<JsonElement[]>($"/api/transactions/{id}/history")).Length.ShouldBe(3);
    }

    [Fact]
    public async Task Invalid_splits_and_splits_on_other_types_are_refused()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Split refusals", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var other = await api.CreateAsync("/api/accounts", new { name = "Split refusals 2", kind = "Bank", currency = "EUR", openingBalance = 0 });

        object[] invalid =
        [
            Expense(bank, "2049-05-01", 100m, Split(Electricity, 60m), Split(Groceries, 39.99m)),     // sum
            Expense(bank, "2049-05-01", 100m, Split(Electricity, 100m)),                              // one line
            Expense(bank, "2049-05-01", 100m, Split(Electricity, 60m), Split(Electricity, 40m)),     // duplicate
            Expense(bank, "2049-05-01", 100m, Split(Electricity, 60m), Split(Salary, 40m)),          // income category
            new
            {
                type = "Expense", occurredOn = "2049-05-01", amount = 100m, accountId = bank, categoryId = Groceries,
                splits = new[] { Split(Electricity, 60m), Split(WaterGas, 40m) },
            },
            new
            {
                type = "Transfer", occurredOn = "2049-05-01", amount = 100m, accountId = bank, counterAccountId = other,
                splits = new[] { Split(Electricity, 60m), Split(WaterGas, 40m) },
            },
            new
            {
                type = "InvestmentContribution", occurredOn = "2049-05-01", amount = 100m, accountId = bank,
                bucketId = SystemCatalog.BucketId("stocks-etfs"), splits = new[] { Split(Electricity, 60m), Split(WaterGas, 40m) },
            },
        ];
        foreach (var body in invalid)
        {
            (await api.PostAsync("/api/transactions", body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(body));
        }

        // Income splits over income categories are fine.
        await api.CreateAsync("/api/transactions", new
        {
            type = "Income", occurredOn = "2049-05-02", amount = 1000m, accountId = bank,
            splits = new[] { Split(Salary, 900m), Split(Bonus, 100m) },
        });
    }

    [Fact]
    public async Task Csv_has_one_row_per_line_with_a_split_marker()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Split csv", kind = "Bank", currency = "EUR", openingBalance = 0 });
        await api.CreateAsync("/api/transactions",
            Expense(bank, "2049-06-03", 100m, Split(Electricity, 60m, "power"), Split(WaterGas, 40m)));

        var csv = await api.Http.GetStringAsync("/api/exports/csv?dataset=Transactions&from=2049-06-01&to=2049-06-30");
        var rows = csv.TrimEnd().Split("\r\n");
        rows[0].ShouldEndWith(",split,split_note");
        rows.Length.ShouldBe(3);
        rows[1].ShouldContain("Electricity,Fixed");
        rows[1].ShouldContain(",60.0000,");
        rows[1].ShouldEndWith(",1/2,power");
        rows[2].ShouldContain("Water & Gas");
        rows[2].ShouldEndWith(",2/2,");
    }

    [Fact]
    public async Task A_split_recurring_item_carries_its_lines_into_the_proposal_and_the_confirmed_transaction()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new { name = "Split recurring", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringId = await api.CreateAsync("/api/recurring", new
        {
            name = "Energy (split)", type = "Expense", amount = 90m, accountId = bank, frequency = "Monthly",
            startOn = today.ToString("yyyy-MM-dd"), splits = new[] { Split(Electricity, 60m), Split(WaterGas, 30m) },
        });

        var template = (await api.GetAsync<JsonElement[]>("/api/recurring")).Single(r => r.GetProperty("id").GetGuid() == recurringId);
        template.GetProperty("splits").GetArrayLength().ShouldBe(2);
        template.GetProperty("categoryId").ValueKind.ShouldBe(JsonValueKind.Null);

        var proposal = (await api.GetAsync<JsonElement[]>("/api/expected"))
            .First(e => e.GetProperty("recurringTransactionId").GetGuid() == recurringId);
        proposal.GetProperty("splits").GetArrayLength().ShouldBe(2);

        var confirm = await api.PostAsync($"/api/expected/{proposal.GetProperty("id").GetGuid()}/confirm", new { amount = 100m });
        await ApiClient.EnsureAsync(confirm);
        var transactionId = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transactionId").GetGuid();
        var transaction = await api.GetJsonAsync($"/api/transactions/{transactionId}");
        transaction.GetProperty("amount").GetDecimal().ShouldBe(100m);
        transaction.GetProperty("splits").EnumerateArray().Select(l => l.GetProperty("amount").GetDecimal()).ShouldBe([66.67m, 33.33m]);

        // A recurring split must add up too.
        (await api.PostAsync("/api/recurring", new
        {
            name = "Bad split", type = "Expense", amount = 90m, accountId = bank, frequency = "Monthly",
            startOn = today.ToString("yyyy-MM-dd"), splits = new[] { Split(Electricity, 60m), Split(WaterGas, 20m) },
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ai_tools_count_lines_show_splits_and_write_them_strictly()
    {
        var owner = await factory.OwnerAsync();
        var bank = await owner.CreateAsync("/api/accounts", new { name = "Split AI bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var other = await owner.CreateAsync("/api/accounts", new { name = "Split AI other", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var token = await TokenAsync(owner, "Split AI", "expenses.summary.read", "transactions.read", "transactions.write");

        // Write: a split expense from "category: amount" lines.
        var (status, body, raw) = await CallAsync(token, "create_transaction", new
        {
            type = "expense", date = "2049-07-04", amount = 100m, account_id = bank, description = "Energy via AI",
            splits = "electricity: 60.00; Water & Gas: 40",
        });
        status.ShouldBe(HttpStatusCode.OK, raw);
        var id = body.GetProperty("data").GetProperty("id").GetGuid();
        (await owner.GetJsonAsync($"/api/transactions/{id}")).GetProperty("splits").GetArrayLength().ShouldBe(2);

        // Read: summaries count the lines, rows list them.
        var (_, summary, _) = await CallAsync(token, "get_expense_summary", new { period = "2049-07", category = "electricity" });
        summary.GetProperty("data").GetProperty("total").GetDecimal().ShouldBe(60m);
        summary.GetProperty("data").GetProperty("transactions").GetInt32().ShouldBe(1);
        var (_, listed, _) = await CallAsync(token, "get_transactions", new { period = "2049-07", category = "water-gas" });
        var data = listed.GetProperty("data");
        data.GetProperty("totalsByType")[0].GetProperty("totalEur").GetDecimal().ShouldBe(40m);
        var item = data.GetProperty("items")[0];
        item.GetProperty("splits").EnumerateArray().Select(s => (s.GetProperty("category").GetString(), s.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Electricity", 60m), ("Water & Gas", 40m)]);

        // Editing a split: the amount alone cannot change (the lines must add up), new lines can.
        (await CallAsync(token, "update_transaction", new { transaction_id = id, amount = 110m })).Status
            .ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(token, "update_transaction", new { transaction_id = id, description = "Energy bill (AI)" })).Status
            .ShouldBe(HttpStatusCode.OK);
        (await owner.GetJsonAsync($"/api/transactions/{id}")).GetProperty("splits").GetArrayLength().ShouldBe(2);
        (await CallAsync(token, "update_transaction", new { transaction_id = id, amount = 110m, splits = "electricity: 70; water-gas: 40" }))
            .Status.ShouldBe(HttpStatusCode.OK);
        (await owner.GetJsonAsync($"/api/transactions/{id}")).GetProperty("splits").EnumerateArray()
            .Select(l => l.GetProperty("amount").GetDecimal()).ShouldBe([70m, 40m]);

        // Strict: sums, formats, duplicates, category + splits, transfers.
        object[] refused =
        [
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity: 60; water-gas: 30" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity 60; water-gas 40" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity: 100" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity: 50; electricity: 50" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity: 60.001; water-gas: 39.999" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, category = "groceries", splits = "electricity: 60; water-gas: 40" },
            new { type = "expense", date = "2049-07-05", amount = 100m, account_id = bank, splits = "electricity: 60; salary: 40" },
            new { type = "transfer", date = "2049-07-05", amount = 100m, account_id = bank, to_account_id = other, splits = "electricity: 60; water-gas: 40" },
        ];
        foreach (var args in refused)
        {
            (await CallAsync(token, "create_transaction", args)).Status.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(args));
        }
    }

    private static async Task<string> TokenAsync(ApiClient owner, string name, params string[] scopes)
    {
        var response = await owner.PostAsync("/api/ai-admin/clients", new { name, scopes, rateLimitPerMinute = 600 });
        await ApiClient.EnsureAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> CallAsync(string token, string tool, object args)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/tools/{tool}") { Content = JsonContent.Create(args) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonSerializer.Deserialize<JsonElement>(raw), raw);
    }
}
