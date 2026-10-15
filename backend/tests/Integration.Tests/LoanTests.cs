using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

/// <summary>Loan accounts with terms: plan-based balance, rate revisions, early repayments and the simulator.</summary>
[Collection(ApiCollection.Name)]
public sealed class LoanTests(ApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_loan_with_terms_owes_what_its_plan_says_and_can_be_simulated()
    {
        var api = await factory.OwnerAsync();
        var loan = await api.CreateAsync("/api/accounts",
            new { name = "Mortgage test", kind = "Loan", currency = "EUR", openingBalance = 0 });
        var first = new DateOnly(Today.Year, Today.Month, 5).AddMonths(-12);
        await ApiClient.EnsureAsync(await api.PutAsync($"/api/loans/{loan}", new
        {
            principal = 100_000m, firstPaymentOn = first.ToString("yyyy-MM-dd"), termMonths = 360,
            rateType = "Fixed", initialRatePercent = 3m,
        }));

        var detail = await api.GetJsonAsync($"/api/loans/{loan}");
        var summary = detail.GetProperty("summary");
        detail.GetProperty("plan").GetArrayLength().ShouldBe(360);
        summary.GetProperty("next").GetProperty("payment").GetDecimal().ShouldBe(421.60m);
        var outstanding = summary.GetProperty("outstanding").GetDecimal();
        outstanding.ShouldBeLessThan(100_000m);
        summary.GetProperty("instalmentsLeft").GetInt32().ShouldBeInRange(347, 348);

        // The account's balance is the plan's debt (no instalment booked).
        var account = (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == loan);
        account.GetProperty("balance").GetDecimal().ShouldBe(-outstanding);

        var shorter = await api.PostAsync($"/api/loans/{loan}/simulate", new
        {
            on = Today.ToString("yyyy-MM-dd"), amount = 20_000m, mode = "ReduceTerm",
        });
        await ApiClient.EnsureAsync(shorter);
        var sim = await shorter.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json);
        sim.GetProperty("monthsSaved").GetInt32().ShouldBeGreaterThan(50);
        sim.GetProperty("interestSaved").GetDecimal().ShouldBeGreaterThan(10_000m);
        sim.GetProperty("paymentAfter").GetDecimal().ShouldBe(421.60m);

        // A real early repayment changes the plan; removing it restores it.
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/loans/{loan}/prepayments", new
        {
            on = Today.ToString("yyyy-MM-dd"), amount = 20_000m, mode = "ReducePayment",
        }));
        detail = await api.GetJsonAsync($"/api/loans/{loan}");
        detail.GetProperty("summary").GetProperty("next").GetProperty("payment").GetDecimal().ShouldBeLessThan(421.60m);
        var prepaymentId = detail.GetProperty("prepayments")[0].GetProperty("id").GetGuid();
        (await api.Http.DeleteAsync($"/api/loans/{loan}/prepayments/{prepaymentId}")).EnsureSuccessStatusCode();
        (await api.GetJsonAsync($"/api/loans/{loan}")).GetProperty("summary").GetProperty("next")
            .GetProperty("payment").GetDecimal().ShouldBe(421.60m);
    }

    [Fact]
    public async Task A_variable_rate_is_index_plus_spread_and_revisions_change_the_instalment()
    {
        var api = await factory.OwnerAsync();
        var loan = await api.CreateAsync("/api/accounts",
            new { name = "Variable mortgage test", kind = "Loan", currency = "EUR", openingBalance = 0 });
        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-3);
        await ApiClient.EnsureAsync(await api.PutAsync($"/api/loans/{loan}", new
        {
            principal = 150_000m, firstPaymentOn = first.ToString("yyyy-MM-dd"), termMonths = 300,
            rateType = "Variable", revisionMonths = 6, indexName = "Euribor 6M", spreadPercent = 1m,
            initialIndexPercent = 2.5m,
        }));
        var before = (await api.GetJsonAsync($"/api/loans/{loan}")).GetProperty("summary");
        before.GetProperty("currentRatePercent").GetDecimal().ShouldBe(3.5m);
        before.GetProperty("nextRevision").GetString().ShouldBe(first.AddMonths(6).ToString("yyyy-MM-dd"));

        // Revision: Euribor up to 3 % → TAN 4 % from the next revision date.
        await ApiClient.EnsureAsync(await api.PostAsync($"/api/loans/{loan}/rates", new
        {
            effectiveFrom = first.AddMonths(6).ToString("yyyy-MM-dd"), indexRatePercent = 3m,
        }));
        var plan = (await api.GetJsonAsync($"/api/loans/{loan}")).GetProperty("plan").EnumerateArray().ToList();
        plan[6].GetProperty("annualRatePercent").GetDecimal().ShouldBe(4m);
        plan[6].GetProperty("payment").GetDecimal().ShouldBeGreaterThan(plan[5].GetProperty("payment").GetDecimal());
    }

    [Fact]
    public async Task Only_loan_accounts_get_terms()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Not a loan", kind = "Bank", currency = "EUR", openingBalance = 0 });
        (await api.PutAsync($"/api/loans/{bank}", new
        {
            principal = 1_000m, firstPaymentOn = "2026-01-01", termMonths = 12, rateType = "Fixed",
            initialRatePercent = 5m,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Instalments_are_proposed_once_a_payment_account_is_set_and_book_interest_and_capital()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Loan payer bank", kind = "Bank", currency = "EUR", openingBalance = 5_000m });
        var loan = await api.CreateAsync("/api/accounts",
            new { name = "Car loan test", kind = "Loan", currency = "EUR", openingBalance = 0 });
        await ApiClient.EnsureAsync(await api.PutAsync($"/api/loans/{loan}", new
        {
            principal = 12_000m, firstPaymentOn = Today.ToString("yyyy-MM-dd"), termMonths = 48, rateType = "Fixed",
            initialRatePercent = 6m,
        }));
        async Task<JsonElement[]> Notifications() =>
            (await api.GetJsonAsync("/api/notifications")).GetProperty("items").EnumerateArray().ToArray();
        (await Notifications()).ShouldNotContain(i => i.GetProperty("id").GetString()!.StartsWith("loan:"));

        await ApiClient.EnsureAsync(await api.PutAsync($"/api/loans/{loan}/payment-account", new { accountId = bank }));
        var item = (await Notifications()).Single(i => i.GetProperty("kind").GetString() == "LoanInstalmentDue" &&
                                                        i.GetProperty("targetId").GetGuid() == loan);
        item.GetProperty("args").GetProperty("number").GetInt32().ShouldBe(1);
        item.GetProperty("args").GetProperty("interest").GetDecimal().ShouldBe(60m); // 12,000 × 6 % / 12
        var payment = item.GetProperty("args").GetProperty("amount").GetDecimal();

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/loans/{loan}/instalments/1/book", new { }));
        (await Notifications()).ShouldNotContain(i => i.GetProperty("kind").GetString() == "LoanInstalmentDue" &&
                                                      i.GetProperty("targetId").GetGuid() == loan);
        var accounts = await api.GetAsync<JsonElement[]>("/api/accounts");
        accounts.Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal()
            .ShouldBe(5_000m - payment);
        // Booking twice is refused.
        (await api.PostAsync($"/api/loans/{loan}/instalments/1/book", new { })).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_revision_date_without_a_rate_is_announced_until_the_rate_is_entered()
    {
        var api = await factory.OwnerAsync();
        var loan = await api.CreateAsync("/api/accounts",
            new { name = "Revision reminder loan", kind = "Loan", currency = "EUR", openingBalance = 0 });
        var first = Today.AddMonths(-6).AddDays(5); // revision in 5 days
        await ApiClient.EnsureAsync(await api.PutAsync($"/api/loans/{loan}", new
        {
            principal = 50_000m, firstPaymentOn = first.ToString("yyyy-MM-dd"), termMonths = 120,
            rateType = "Variable", revisionMonths = 6, indexName = "Euribor 6M", spreadPercent = 1m,
            initialIndexPercent = 2m,
        }));
        var revision = first.AddMonths(6).ToString("yyyy-MM-dd");
        async Task<bool> Announced() => (await api.GetJsonAsync("/api/notifications")).GetProperty("items")
            .EnumerateArray().Any(i => i.GetProperty("kind").GetString() == "LoanRateRevision" &&
                                       i.GetProperty("targetId").GetGuid() == loan);
        (await Announced()).ShouldBeTrue();

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/loans/{loan}/rates",
            new { effectiveFrom = revision, indexRatePercent = 2.4m }));
        (await Announced()).ShouldBeFalse();
    }
}
