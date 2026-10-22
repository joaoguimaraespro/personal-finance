using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Tests.Infrastructure;
using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace Integration.Tests;

/// <summary>
/// A broker account linked to an allocation bucket: the deposits its sync reports count towards that bucket each
/// month, and money also recorded in the ledger as going to that broker is counted once.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BrokerAllocationTests(ApiFactory factory)
{
    private static async Task WaitForSyncAsync(ApiClient api, Guid connectionId)
    {
        for (var i = 0; i < 120; i++)
        {
            var jobs = await api.GetAsync<JsonElement[]>($"/api/integrations/connections/{connectionId}/jobs");
            if (jobs.Any(j => j.GetProperty("outcome").GetString() != "Running"))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Sync did not finish.");
    }

    /// <summary>The demo broker A of the portfolio tests (created the same way when this test runs first).</summary>
    private static async Task<Guid> DemoBrokerAccountAsync(ApiClient api)
    {
        var connections = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
        if (connections.Length < 2)
        {
            foreach (var (name, profile) in new[] { ("Demo broker A", "a"), ("Demo broker B", "b") })
            {
                var id = await api.CreateAsync("/api/integrations/connections", new
                {
                    kind = "Demo", displayName = name, credentials = new Dictionary<string, string> { ["profile"] = profile },
                });
                await WaitForSyncAsync(api, id);
            }

            connections = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
        }

        var a = connections.First(c => c.GetProperty("displayName").GetString() == "Demo broker A");
        await WaitForSyncAsync(api, a.GetProperty("id").GetGuid());
        return a.GetProperty("accountId").GetGuid();
    }

    [Fact]
    public async Task Synced_deposits_count_towards_the_linked_bucket_once()
    {
        var api = await factory.OwnerAsync();
        var broker = await DemoBrokerAccountAsync(api);
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Broker-fed ETFs", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Broker allocation bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Demo broker A deposits 600 every month; two months back is older than the review window, so it counts as is.
        var last = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var period = $"{last:yyyy-MM}";
        async Task<JsonElement> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{period}");
            return report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Single(b => b.GetProperty("bucketId").GetGuid() == bucket);
        }
        async Task Link(Guid? bucketId) => await ApiClient.EnsureAsync(
            await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId }));

        try
        {
            await Link(bucket);
            var line = await Line();
            line.GetProperty("actual").GetDecimal().ShouldBe(600m);
            line.GetProperty("fromBrokers").GetDecimal().ShouldBe(600m);

            // The same money recorded in the ledger as going to that broker: counted once, not 1,000.
            await api.CreateAsync("/api/transactions", new
            {
                type = "InvestmentContribution", occurredOn = $"{last:yyyy-MM}-02", amount = 400m, accountId = bank,
                bucketId = bucket, counterAccountId = broker,
            });
            line = await Line();
            line.GetProperty("actual").GetDecimal().ShouldBe(600m);
            line.GetProperty("fromBrokers").GetDecimal().ShouldBe(200m);

            // Unlinked: only the ledger counts.
            await Link(null);
            (await Line()).GetProperty("actual").GetDecimal().ShouldBe(400m);

            var account = (await api.GetAsync<JsonElement[]>("/api/accounts"))
                .Single(a => a.GetProperty("id").GetGuid() == broker);
            account.GetProperty("allocationBucketId").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId = (Guid?)null });
        }
    }

    [Fact]
    public async Task A_recent_deposit_waits_for_the_account_it_came_from()
    {
        var api = await factory.OwnerAsync();
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Reviewed ETFs", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Deposit source bank", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var kind = $"DEPOSIT-{Guid.NewGuid():N}"[..20];
        Guid broker;
        using (var scope = factory.Services.CreateScope())
        {
            // A broker account of its own (as a connection creates it, without positions) and the deposit a sync
            // records when the money arrives today: no other test sees either.
            var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var account = Finance.Domain.Accounts.Account.Create("Reviewed broker", Finance.Domain.Accounts.AccountKind.Broker,
                "EUR", 0m, today.AddYears(-1));
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(CancellationToken.None);
            broker = account.Id;
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            db.CashMovements.Add(CashMovement.Record(broker, CashMovementType.Deposit, 500m, "EUR", 500m, now, kind,
                DataSource.Demo, $"test:{kind}", now));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        async Task<JsonElement?> Movement() => (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
            .Cast<JsonElement?>().SingleOrDefault(m => m!.Value.GetProperty("kind").GetString() == kind);
        async Task<(decimal Actual, decimal Pending)> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
            var line = report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Cast<JsonElement?>().SingleOrDefault(b => b!.Value.GetProperty("bucketId").GetGuid() == bucket);
            return line is { } l
                ? (l.GetProperty("actual").GetDecimal(), l.GetProperty("pending").GetDecimal())
                : (0m, 0m);
        }
        async Task<bool> Notified(Guid id) => (await api.GetJsonAsync("/api/notifications")).GetProperty("items")
            .EnumerateArray().Any(i => i.GetProperty("kind").GetString() == "BrokerMovement" &&
                                       i.GetProperty("targetId").GetGuid() == id);
        async Task<decimal> BankBalance() => (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal();

        try
        {
            await ApiClient.EnsureAsync(
                await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId = bucket }));
            var movement = (await Movement())!.Value;
            var id = movement.GetProperty("id").GetGuid();
            movement.GetProperty("state").GetString().ShouldBe("Pending");

            // Not counted until the owner says where the money came from: shown apart, and asked about.
            (await Line()).ShouldBe((0m, 500m));
            (await Notified(id)).ShouldBeTrue();

            // Recorded from the bank, for less (part of it went elsewhere): the bucket counts what was recorded.
            var response = await api.PostAsync($"/api/broker-movements/{id}/record", new
            {
                type = "InvestmentContribution", occurredOn = $"{today:yyyy-MM-dd}", amount = 487.01m, accountId = bank,
            });
            await ApiClient.EnsureAsync(response);
            var transactionId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await Line()).ShouldBe((487.01m, 0m));
            (await BankBalance()).ShouldBe(512.99m);
            (await Notified(id)).ShouldBeFalse();
            movement = (await Movement())!.Value;
            movement.GetProperty("state").GetString().ShouldBe("Recorded");
            movement.GetProperty("transactionId").GetGuid().ShouldBe(transactionId);
            var tx = await api.GetJsonAsync($"/api/transactions/{transactionId}");
            tx.GetProperty("counterAccountId").GetGuid().ShouldBe(broker);
            tx.GetProperty("bucketId").GetGuid().ShouldBe(bucket);

            // Recording it twice is refused.
            (await api.PostAsync($"/api/broker-movements/{id}/record", new
            {
                type = "InvestmentContribution", occurredOn = $"{today:yyyy-MM-dd}", amount = 1m, accountId = bank,
            })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

            // Its transaction deleted: the question comes back.
            await ApiClient.EnsureAsync(await api.Http.DeleteAsync($"/api/transactions/{transactionId}"));
            (await Movement())!.Value.GetProperty("state").GetString().ShouldBe("Pending");
            (await Notified(id)).ShouldBeTrue();

            // Ignored (e.g. already recorded by hand): neither counted nor asked about.
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{id}/ignore", new { }));
            (await Movement())!.Value.GetProperty("state").GetString().ShouldBe("Ignored");
            (await Line()).ShouldBe((0m, 0m));
            (await Notified(id)).ShouldBeFalse();
        }
        finally
        {
            // Gone again, so the portfolio tests see only their own brokers.
            using var scope = factory.Services.CreateScope();
            var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            await db.CashMovements.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
            await db.PortfolioSnapshots.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
            await finance.Transactions.IgnoreQueryFilters()
                .Where(t => t.AccountId == broker || t.CounterAccountId == broker).ExecuteDeleteAsync();
            await finance.Accounts.Where(a => a.Id == broker).ExecuteDeleteAsync();
        }
    }

    /// <summary>A broker account of its own, linked to a bucket, with what its sync recorded today.</summary>
    private async Task<Guid> SeedBrokerAsync(ApiClient api, Guid bucket, params (decimal Amount, string Kind)[] movements)
    {
        var now = DateTimeOffset.UtcNow;
        Guid broker;
        using (var scope = factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var account = Finance.Domain.Accounts.Account.Create("Card broker", Finance.Domain.Accounts.AccountKind.Broker,
                "EUR", 0m, DateOnly.FromDateTime(now.UtcDateTime).AddYears(-1));
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(CancellationToken.None);
            broker = account.Id;
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            foreach (var (amount, kind) in movements)
            {
                db.CashMovements.Add(CashMovement.Record(broker,
                    amount > 0 ? CashMovementType.Deposit : CashMovementType.Withdrawal, amount, "EUR", amount, now, kind,
                    DataSource.Demo, $"test:{kind}", now));
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }

        await ApiClient.EnsureAsync(
            await api.PutAsync($"/api/accounts/{broker}/allocation-bucket", new { bucketId = bucket }));
        return broker;
    }

    /// <summary>Removes the seeded broker and everything recorded on it, so no other test sees it.</summary>
    private async Task RemoveBrokerAsync(Guid broker)
    {
        using var scope = factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
        var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
        await db.CashMovements.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
        await db.PortfolioSnapshots.Where(c => c.AccountId == broker).ExecuteDeleteAsync();
        var traded = db.Trades.Where(t => t.AccountId == broker).Select(t => t.SecurityId);
        var securities = await traded.Distinct().ToListAsync();
        await db.Trades.Where(t => t.AccountId == broker).ExecuteDeleteAsync();
        await db.Securities.Where(x => securities.Contains(x.Id)).ExecuteDeleteAsync();
        var onBroker = finance.Transactions.IgnoreQueryFilters()
            .Where(t => t.AccountId == broker || t.CounterAccountId == broker).Select(t => t.Id);
        await finance.Transactions.IgnoreQueryFilters().Where(t => onBroker.Contains(t.SharedFromId!.Value))
            .ExecuteDeleteAsync();
        await finance.Transactions.IgnoreQueryFilters()
            .Where(t => t.AccountId == broker || t.CounterAccountId == broker).ExecuteDeleteAsync();
        await finance.Accounts.Where(a => a.Id == broker).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_card_payment_is_an_expense_paid_by_the_broker_and_comes_off_what_was_invested()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var bucket = await api.CreateAsync("/api/buckets", new { name = $"Card ETFs {tag}", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = $"Card test bank {tag}", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var ana = await api.CreateAsync("/api/accounts",
            new { name = $"Ana (streaming) {tag}", kind = "Person", currency = "EUR", openingBalance = 0m });
        var broker = await SeedBrokerAsync(api, bucket,
            (500m, $"DEPOSIT-{tag}"), (-12.99m, $"WITHDRAW-{tag}"), (-20m, $"WITHDRAW-B-{tag}"));
        async Task<JsonElement> Movement(string kind) => (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
            .Single(m => m.GetProperty("kind").GetString() == kind);
        async Task<decimal> Balance(Guid id) => (await api.GetAsync<JsonElement[]>("/api/accounts?includePeople=true"))
            .Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("balance").GetDecimal();
        async Task<JsonElement> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
            return report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Single(b => b.GetProperty("bucketId").GetGuid() == bucket);
        }

        try
        {
            // The deposit came from the bank.
            var deposit = (await Movement($"DEPOSIT-{tag}")).GetProperty("id").GetGuid();
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{deposit}/record", new
            {
                type = "InvestmentContribution", occurredOn = $"{today:yyyy-MM-dd}", amount = 500m, accountId = bank,
            }));

            // A streaming bill shared half with Ana, due today, was paid with the broker's card.
            var recurring = await api.CreateAsync("/api/recurring", new
            {
                name = $"Streaming {tag}", type = "Expense", amount = 12.99m, accountId = bank, frequency = "Monthly",
                startOn = $"{today:yyyy-MM-dd}", dayOfMonth = today.Day,
                categoryId = Finance.Domain.SystemCatalog.CategoryId("subscriptions"),
                shares = new[] { new { personId = ana, percent = 50m } },
            });
            var card = await Movement($"WITHDRAW-{tag}");
            card.GetProperty("state").GetString().ShouldBe("Pending");
            var cardId = card.GetProperty("id").GetGuid();
            var candidates = await api.GetJsonAsync($"/api/broker-movements/{cardId}/card-payment");
            var due = candidates.GetProperty("recurring").EnumerateArray()
                .Single(r => r.GetProperty("name").GetString() == $"Streaming {tag}");
            due.GetProperty("shared").GetBoolean().ShouldBeTrue();
            var expected = due.GetProperty("expectedId").GetGuid();

            var response = await api.PostAsync($"/api/broker-movements/{cardId}/card-payment", new { expectedId = expected });
            await ApiClient.EnsureAsync(response);
            var expenseId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            // Paid from the broker: the bank only lost the deposit, Ana owes her half, and the month counts the
            // expense and 12.99 less invested.
            (await Balance(bank)).ShouldBe(500m);
            (await Balance(ana)).ShouldBe(6.49m); // her half, the odd cent stays with the owner
            var expense = await api.GetJsonAsync($"/api/transactions/{expenseId}");
            expense.GetProperty("accountId").GetGuid().ShouldBe(broker);
            var line = await Line();
            line.GetProperty("actual").GetDecimal().ShouldBe(487.01m);
            line.GetProperty("spent").GetDecimal().ShouldBe(12.99m);
            card = await Movement($"WITHDRAW-{tag}");
            card.GetProperty("state").GetString().ShouldBe("Recorded");
            card.GetProperty("cardPayment").GetBoolean().ShouldBeTrue();
            (await api.GetAsync<JsonElement[]>("/api/expected"))
                .Any(p => p.GetProperty("id").GetGuid() == expected).ShouldBeFalse();

            // An expense first booked on the bank was the other card payment: it moves to the broker.
            var lunch = await api.CreateAsync("/api/transactions", new
            {
                type = "Expense", occurredOn = $"{today:yyyy-MM-dd}", amount = 20m, accountId = bank,
                categoryId = Finance.Domain.SystemCatalog.CategoryId("restaurants"), description = $"Lunch {tag}",
            });
            (await Balance(bank)).ShouldBe(480m);
            var second = (await Movement($"WITHDRAW-B-{tag}")).GetProperty("id").GetGuid();
            (await api.GetJsonAsync($"/api/broker-movements/{second}/card-payment")).GetProperty("expenses")
                .EnumerateArray().Any(e => e.GetProperty("transactionId").GetGuid() == lunch).ShouldBeTrue();
            await ApiClient.EnsureAsync(
                await api.PostAsync($"/api/broker-movements/{second}/card-payment", new { transactionId = lunch }));
            (await Balance(bank)).ShouldBe(500m);
            (await Line()).GetProperty("actual").GetDecimal().ShouldBe(467.01m);

            // Still editable on the broker; but an expense typed in by hand on a broker stays refused.
            await ApiClient.EnsureAsync(await api.PutAsync($"/api/transactions/{lunch}", new
            {
                type = "Expense", occurredOn = $"{today:yyyy-MM-dd}", amount = 20m, accountId = broker,
                categoryId = Finance.Domain.SystemCatalog.CategoryId("groceries"), description = $"Lunch {tag}",
            }));
            (await api.PostAsync("/api/transactions", new
            {
                type = "Expense", occurredOn = $"{today:yyyy-MM-dd}", amount = 5m, accountId = broker,
                categoryId = Finance.Domain.SystemCatalog.CategoryId("groceries"),
            })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            // A deposit is never a card payment.
            (await api.PostAsync($"/api/broker-movements/{deposit}/card-payment",
                new { categoryId = Finance.Domain.SystemCatalog.CategoryId("groceries") })).IsSuccessStatusCode
                .ShouldBeFalse();
            await api.Http.DeleteAsync($"/api/recurring/{recurring}");
        }
        finally
        {
            await RemoveBrokerAsync(broker);
        }
    }

    [Fact]
    public async Task Cash_transferred_to_a_broker_counts_as_invested_when_its_buys_spend_it()
    {
        var api = await factory.OwnerAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var bucket = await api.CreateAsync("/api/buckets", new { name = $"Cash ETFs {tag}", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = $"Transfer test bank {tag}", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var card = await api.CreateAsync("/api/accounts",
            new { name = $"Broker card pot {tag}", kind = "Bank", currency = "EUR", openingBalance = 0m });
        var broker = await SeedBrokerAsync(api, bucket, (500m, $"DEPOSIT-{tag}"), (-12.99m, $"WITHDRAW-{tag}"));
        async Task<JsonElement> Movement(string kind) => (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
            .Single(m => m.GetProperty("kind").GetString() == kind);
        async Task<decimal> Balance(Guid id) => (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("balance").GetDecimal();
        async Task<(decimal Actual, decimal Pending)> Line()
        {
            var report = await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
            var line = report.GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Cast<JsonElement?>().SingleOrDefault(b => b!.Value.GetProperty("bucketId").GetGuid() == bucket);
            return line is { } l
                ? (l.GetProperty("actual").GetDecimal(), l.GetProperty("pending").GetDecimal())
                : (0m, 0m);
        }
        async Task BuyAsync(decimal amount)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            var security = Security.Create(null, $"T{Guid.NewGuid():N}"[..10], null, "Test ETF", "EUR", AssetClass.Etf);
            db.Securities.Add(security);
            db.Trades.Add(Trade.Record(broker, security.Id, TradeSide.Buy, 1m, amount, "EUR", 0m, 0m, 0m, -amount,
                null, now, DataSource.Demo, $"test:{tag}:{Guid.NewGuid():N}"));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        try
        {
            // The deposit came from the bank, to wait at the broker as cash: the bank pays, nothing invested yet.
            var deposit = (await Movement($"DEPOSIT-{tag}")).GetProperty("id").GetGuid();
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{deposit}/record", new
            {
                type = "Transfer", occurredOn = $"{today:yyyy-MM-dd}", amount = 500m, accountId = bank,
                counterAccountId = broker,
            }));
            (await Balance(bank)).ShouldBe(500m);
            var recorded = await Movement($"DEPOSIT-{tag}");
            recorded.GetProperty("state").GetString().ShouldBe("Recorded");
            recorded.GetProperty("recordedAs").GetString().ShouldBe("Transfer");

            // The withdrawal went to the card's own account; the form may name it on either side.
            var withdrawal = (await Movement($"WITHDRAW-{tag}")).GetProperty("id").GetGuid();
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{withdrawal}/record", new
            {
                type = "Transfer", occurredOn = $"{today:yyyy-MM-dd}", amount = 12.99m, accountId = card,
                counterAccountId = broker,
            }));
            (await Balance(card)).ShouldBe(12.99m);
            (await Line()).ShouldBe((0m, 0m));

            // Buys spend the cash waiting there, and only up to it.
            await BuyAsync(300m);
            (await Line()).ShouldBe((300m, 0m));
            await BuyAsync(250m);
            (await Line()).ShouldBe((487.01m, 0m));

            // A transfer needs the owner's own account.
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{withdrawal}/reopen", new { }));
            (await api.PostAsync($"/api/broker-movements/{withdrawal}/record", new
            {
                type = "Transfer", occurredOn = $"{today:yyyy-MM-dd}", amount = 12.99m, accountId = broker,
            })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            // Undone: asked again, while the transfer it was recorded as stays in the ledger.
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{deposit}/reopen", new { }));
            var reopened = await Movement($"DEPOSIT-{tag}");
            reopened.GetProperty("state").GetString().ShouldBe("Pending");
            reopened.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
            (await Balance(bank)).ShouldBe(500m);
            (await Line()).ShouldBe((0m, 487.01m));
        }
        finally
        {
            await RemoveBrokerAsync(broker);
        }
    }

    [Fact]
    public async Task A_card_payment_spends_transferred_cash_and_the_buy_counts_in_full()
    {
        var api = await factory.OwnerAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var bucket = await api.CreateAsync("/api/buckets", new { name = $"Card cash ETFs {tag}", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = $"Card cash bank {tag}", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var broker = await SeedBrokerAsync(api, bucket, (500m, $"DEPOSIT-{tag}"), (-12.99m, $"WITHDRAW-{tag}"));
        async Task<JsonElement> Movement(string kind) => (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
            .Single(m => m.GetProperty("kind").GetString() == kind);
        async Task<decimal> BankBalance() => (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == bank).GetProperty("balance").GetDecimal();
        async Task<JsonElement> Report() => await api.GetJsonAsync($"/api/reports/monthly/{today:yyyy-MM}");
        // A bucket with nothing invested, nothing taken off that, and nothing left to record is left out of the month.
        async Task<(decimal Actual, decimal Spent)> Line()
        {
            var line = (await Report()).GetProperty("current").GetProperty("buckets").EnumerateArray()
                .Cast<JsonElement?>().SingleOrDefault(b => b!.Value.GetProperty("bucketId").GetGuid() == bucket);
            return line is { } l
                ? (l.GetProperty("actual").GetDecimal(), l.GetProperty("spent").GetDecimal())
                : (0m, 0m);
        }
        async Task BuyAsync(decimal amount)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
            var security = Security.Create(null, $"T{Guid.NewGuid():N}"[..10], null, "Test ETF", "EUR", AssetClass.Etf);
            db.Securities.Add(security);
            db.Trades.Add(Trade.Record(broker, security.Id, TradeSide.Buy, 1m, amount, "EUR", 0m, 0m, 0m, -amount,
                null, now, DataSource.Demo, $"test:{tag}:{Guid.NewGuid():N}"));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        try
        {
            var deposit = (await Movement($"DEPOSIT-{tag}")).GetProperty("id").GetGuid();
            await ApiClient.EnsureAsync(await api.PostAsync($"/api/broker-movements/{deposit}/record", new
            {
                type = "Transfer", occurredOn = $"{today:yyyy-MM-dd}", amount = 500m, accountId = bank,
                counterAccountId = broker,
            }));
            (await BankBalance()).ShouldBe(500m);
            (await Line()).ShouldBe((0m, 0m));

            // The card charge is an expense. With nothing invested yet, it only reduces cash still waiting.
            var before = (await Report()).GetProperty("current").GetProperty("totalExpenses").GetDecimal();
            var cardId = (await Movement($"WITHDRAW-{tag}")).GetProperty("id").GetGuid();
            var response = await api.PostAsync($"/api/broker-movements/{cardId}/card-payment", new
            {
                categoryId = Finance.Domain.SystemCatalog.CategoryId("subscriptions"),
                description = $"Netflix {tag}",
            });
            await ApiClient.EnsureAsync(response);
            var expenseId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var expense = await api.GetJsonAsync($"/api/transactions/{expenseId}");
            expense.GetProperty("type").GetString().ShouldBe("Expense");
            expense.GetProperty("accountId").GetGuid().ShouldBe(broker);
            expense.GetProperty("amount").GetDecimal().ShouldBe(12.99m);
            (await BankBalance()).ShouldBe(500m);
            // Nothing is invested yet. The charge is the expense above; it only reduces cash still waiting,
            // so the allocation does not show it as spent.
            (await Line()).ShouldBe((0m, 0m));
            ((await Report()).GetProperty("current").GetProperty("totalExpenses").GetDecimal()).ShouldBe(before + 12.99m);

            // The buy of what is left counts in full. The card is not taken off that figure a second time.
            await BuyAsync(487.01m);
            (await Line()).ShouldBe((487.01m, 0m));
            ((await Report()).GetProperty("current").GetProperty("totalExpenses").GetDecimal()).ShouldBe(before + 12.99m);
        }
        finally
        {
            await RemoveBrokerAsync(broker);
        }
    }

    [Fact]
    public async Task A_recurring_bill_far_from_the_card_charge_is_not_that_payment()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var bucket = await api.CreateAsync("/api/buckets", new { name = $"Card match ETFs {tag}", group = "Investment" });
        var bank = await api.CreateAsync("/api/accounts",
            new { name = $"Card match bank {tag}", kind = "Bank", currency = "EUR", openingBalance = 1000m });
        var broker = await SeedBrokerAsync(api, bucket, (-12.99m, $"WITHDRAW-{tag}"));
        var rent = await api.CreateAsync("/api/recurring", new
        {
            name = $"Rent {tag}", type = "Expense", amount = 900m, accountId = bank, frequency = "Monthly",
            startOn = $"{today:yyyy-MM-dd}", dayOfMonth = today.Day,
            categoryId = Finance.Domain.SystemCatalog.CategoryId("housing"),
        });
        var near = await api.CreateAsync("/api/recurring", new
        {
            name = $"Near bill {tag}", type = "Expense", amount = 12m, accountId = bank, frequency = "Monthly",
            startOn = $"{today:yyyy-MM-dd}", dayOfMonth = today.Day,
            categoryId = Finance.Domain.SystemCatalog.CategoryId("subscriptions"),
        });
        async Task<(Guid Id, decimal Amount, string Status)> Occurrence(Guid recurringId)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Finance.Application.Abstractions.IFinanceDb>();
            var row = await db.ExpectedTransactions.AsNoTracking()
                .Where(e => e.RecurringTransactionId == recurringId)
                .Select(e => new { e.Id, e.Amount, e.Status })
                .SingleAsync();
            return (row.Id, row.Amount, row.Status.ToString());
        }

        try
        {
            var cardId = (await api.GetAsync<JsonElement[]>("/api/broker-movements"))
                .Single(m => m.GetProperty("kind").GetString() == $"WITHDRAW-{tag}").GetProperty("id").GetGuid();
            var candidates = await api.GetJsonAsync($"/api/broker-movements/{cardId}/card-payment");
            var recurring = candidates.GetProperty("recurring").EnumerateArray().ToList();
            recurring.ShouldNotContain(r => r.GetProperty("name").GetString() == $"Rent {tag}");
            var offered = recurring.Single(r => r.GetProperty("name").GetString() == $"Near bill {tag}");
            offered.GetProperty("amount").GetDecimal().ShouldBe(12m);

            var rentDue = await Occurrence(rent);
            rentDue.Amount.ShouldBe(900m);
            rentDue.Status.ShouldBe("Pending");
            var refused = await api.PostAsync($"/api/broker-movements/{cardId}/card-payment",
                new { expectedId = rentDue.Id });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Occurrence(rent)).Status.ShouldBe("Pending");
            (await Occurrence(rent)).Amount.ShouldBe(900m);

            // Close enough: confirmed at the bill's 12.00, not rewritten to the card's 12.99.
            var response = await api.PostAsync($"/api/broker-movements/{cardId}/card-payment",
                new { expectedId = offered.GetProperty("expectedId").GetGuid() });
            await ApiClient.EnsureAsync(response);
            var expenseId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            (await api.GetJsonAsync($"/api/transactions/{expenseId}")).GetProperty("amount").GetDecimal().ShouldBe(12m);
            (await Occurrence(near)).Status.ShouldBe("Confirmed");
            (await Occurrence(near)).Amount.ShouldBe(12m);
            (await Occurrence(rent)).Status.ShouldBe("Pending");
            (await Occurrence(rent)).Amount.ShouldBe(900m);
        }
        finally
        {
            await api.Http.DeleteAsync($"/api/recurring/{rent}");
            await api.Http.DeleteAsync($"/api/recurring/{near}");
            await RemoveBrokerAsync(broker);
        }
    }

    [Fact]
    public async Task Only_broker_accounts_can_be_linked()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts",
            new { name = "Not a broker", kind = "Bank", currency = "EUR", openingBalance = 0 });
        var bucket = await api.CreateAsync("/api/buckets", new { name = "Bank-fed", group = "Investment" });

        (await api.PutAsync($"/api/accounts/{bank}/allocation-bucket", new { bucketId = bucket })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }
}
