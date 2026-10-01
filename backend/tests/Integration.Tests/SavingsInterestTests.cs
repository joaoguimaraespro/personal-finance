using System.Net;
using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class SavingsInterestTests(ApiFactory factory)
{
    private static readonly Guid Interest = SystemCatalog.CategoryId("interest");

    [Fact]
    public async Task Estimated_interest_is_in_the_balance_and_reconciling_replaces_it_without_double_counting()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = new DateOnly(today.Year, today.Month, 1);
        var m1 = current.AddMonths(-3);
        var m2 = current.AddMonths(-2);
        var m3 = current.AddMonths(-1);

        var savings = await api.CreateAsync("/api/accounts", new
        {
            name = "Revolut Savings", kind = "Savings", currency = "EUR", openingBalance = 10_000m,
            openingBalanceOn = Iso(m1), institution = "Revolut", interestPayout = "Monthly",
        });
        var main = await api.CreateAsync("/api/accounts",
            new { name = "Main", kind = "Bank", currency = "EUR", openingBalance = 5_000m, openingBalanceOn = Iso(m1) });
        await api.CreateAsync($"/api/accounts/{savings}/interest-rates",
            new { annualRatePercent = 2.25m, effectiveFrom = Iso(m1) });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Transfer", occurredOn = Iso(m2.AddDays(9)), amount = 2_000m, accountId = main,
            counterAccountId = savings,
        });

        // Rate history and the card summary.
        var rates = await api.GetAsync<JsonElement[]>($"/api/accounts/{savings}/interest-rates");
        rates.ShouldHaveSingleItem().GetProperty("withholdingPercent").GetDecimal().ShouldBe(28m);
        var account = await AccountAsync(api, savings);
        var interest = account.GetProperty("interest");
        interest.GetProperty("annualRatePercent").GetDecimal().ShouldBe(2.25m);
        var estimated = interest.GetProperty("estimatedInBalance").GetDecimal();
        estimated.ShouldBeGreaterThan(0m);
        account.GetProperty("balance").GetDecimal().ShouldBe(12_000m + estimated);

        // One estimated, read-only entry per month (three closed months + the current one).
        var estimates = await EstimatesAsync(api, savings);
        estimates.Length.ShouldBe(4);
        estimates.ShouldAllBe(e => !e.GetProperty("editable").GetBoolean());
        estimates.ShouldAllBe(e => e.GetProperty("categoryKey").GetString() == "interest");
        var first = estimates.Single(e => e.GetProperty("occurredOn").GetString() == Iso(m1.AddMonths(1).AddDays(-1)));
        var m1Days = DateTime.DaysInMonth(m1.Year, m1.Month);
        first.GetProperty("amount").GetDecimal()
            .ShouldBe(Math.Round(10_000m * 2.25m / 100m / 365m * m1Days * 0.72m, 2, MidpointRounding.AwayFromZero));
        (await api.PutAsync($"/api/transactions/{first.GetProperty("id").GetGuid()}", new
        {
            type = "Income", occurredOn = Iso(m1), amount = 99m, accountId = savings, categoryId = Interest,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.Http.DeleteAsync($"/api/transactions/{first.GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Closed months ask to be reconciled; the open month does not.
        var pending = await PendingAsync(api, savings);
        pending.Select(p => p.GetProperty("month").GetString()).ShouldBe([Ym(m1), Ym(m2), Ym(m3)]);

        // The bank paid a different amount in the first month: it replaces the estimate.
        var reconcile = await api.PostAsync($"/api/interest/{pending[0].GetProperty("id").GetGuid()}/reconcile",
            new { amount = 12.34m });
        await ApiClient.EnsureAsync(reconcile);
        // The second month is confirmed as estimated.
        var m2Estimate = pending[1].GetProperty("estimatedAmount").GetDecimal();
        await ApiClient.EnsureAsync(
            await api.PostAsync($"/api/interest/{pending[1].GetProperty("id").GetGuid()}/reconcile", new { }));
        // The third month's real interest arrives as a normal entry (e.g. typed from the bank statement).
        await api.CreateAsync("/api/transactions", new
        {
            type = "Income", occurredOn = Iso(m3.AddDays(27)), amount = 15m, accountId = savings, categoryId = Interest,
        });

        (await PendingAsync(api, savings)).ShouldBeEmpty();
        var interestRows = await InterestRowsAsync(api, savings);
        RowsIn(interestRows, m1).ShouldHaveSingleItem().GetProperty("amount").GetDecimal().ShouldBe(12.34m);
        RowsIn(interestRows, m1).Single().GetProperty("source").GetString().ShouldBe("Manual");
        RowsIn(interestRows, m2).ShouldHaveSingleItem().GetProperty("amount").GetDecimal().ShouldBe(m2Estimate);
        RowsIn(interestRows, m3).ShouldHaveSingleItem().GetProperty("amount").GetDecimal().ShouldBe(15m);
        RowsIn(interestRows, current).ShouldHaveSingleItem().GetProperty("source").GetString()
            .ShouldBe("InterestEstimate");

        // No double counting: the balance is principal plus exactly one interest figure per month.
        account = await AccountAsync(api, savings);
        var interestTotal = interestRows.Sum(r => r.GetProperty("amount").GetDecimal());
        account.GetProperty("balance").GetDecimal().ShouldBe(12_000m + interestTotal);
        account.GetProperty("interest").GetProperty("estimatedInBalance").GetDecimal()
            .ShouldBe(RowsIn(interestRows, current).Single().GetProperty("amount").GetDecimal());

        // A reconciled month cannot be reconciled twice; the open month cannot be reconciled yet.
        (await api.PostAsync($"/api/interest/{pending[0].GetProperty("id").GetGuid()}/reconcile", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var months = await api.GetAsync<JsonElement[]>($"/api/interest/accounts/{savings}");
        var open = months.Single(m => m.GetProperty("month").GetString() == Ym(current));
        (await api.PostAsync($"/api/interest/{open.GetProperty("id").GetGuid()}/reconcile", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        months.Single(m => m.GetProperty("month").GetString() == Ym(m1)).GetProperty("status").GetString()
            .ShouldBe("Corrected");
        months.Single(m => m.GetProperty("month").GetString() == Ym(m2)).GetProperty("status").GetString()
            .ShouldBe("Confirmed");
    }

    [Fact]
    public async Task Rates_are_append_only_and_only_for_savings_or_bank_accounts()
    {
        var api = await factory.OwnerAsync();
        var cash = await api.CreateAsync("/api/accounts",
            new { name = "Wallet", kind = "Cash", currency = "EUR", openingBalance = 50m });
        (await api.PostAsync($"/api/accounts/{cash}/interest-rates",
            new { annualRatePercent = 2m, effectiveFrom = "2026-01-01" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var savings = await api.CreateAsync("/api/accounts",
            new { name = "Deposit", kind = "Savings", currency = "EUR", openingBalance = 1_000m, openingBalanceOn = "2026-01-01" });
        var older = await api.CreateAsync($"/api/accounts/{savings}/interest-rates",
            new { annualRatePercent = 2m, effectiveFrom = "2026-01-01", withholdingPercent = 28m });
        await api.CreateAsync($"/api/accounts/{savings}/interest-rates",
            new { annualRatePercent = 1.5m, effectiveFrom = "2026-06-01" });
        (await api.PostAsync($"/api/accounts/{savings}/interest-rates",
            new { annualRatePercent = 3m, effectiveFrom = "2026-06-01" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await api.Http.DeleteAsync($"/api/accounts/{savings}/interest-rates/{older}"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var history = await api.GetAsync<JsonElement[]>($"/api/accounts/{savings}/interest-rates");
        history.Select(h => h.GetProperty("annualRatePercent").GetDecimal()).ShouldBe([1.5m, 2m]);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static string Ym(DateOnly d) => d.ToString("yyyy-MM");

    private static JsonElement[] RowsIn(JsonElement[] rows, DateOnly month) =>
        rows.Where(r => r.GetProperty("occurredOn").GetString()!.StartsWith(Ym(month), StringComparison.Ordinal))
            .ToArray();

    private static async Task<JsonElement> AccountAsync(ApiClient api, Guid id) =>
        (await api.GetAsync<JsonElement[]>("/api/accounts")).Single(a => a.GetProperty("id").GetGuid() == id);

    private static async Task<JsonElement[]> EstimatesAsync(ApiClient api, Guid account) =>
        (await api.GetJsonAsync($"/api/transactions?accountId={account}&source=InterestEstimate&pageSize=200"))
        .GetProperty("items").EnumerateArray().ToArray();

    private static async Task<JsonElement[]> InterestRowsAsync(ApiClient api, Guid account) =>
        (await api.GetJsonAsync($"/api/transactions?accountId={account}&categoryId={Interest}&pageSize=200"))
        .GetProperty("items").EnumerateArray().ToArray();

    private static async Task<JsonElement[]> PendingAsync(ApiClient api, Guid account) =>
        (await api.GetAsync<JsonElement[]>("/api/interest/pending"))
        .Where(p => p.GetProperty("accountId").GetGuid() == account).ToArray();
}
