using System.Net;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>A broker account takes no entries by hand except money moved out of it into one of your accounts.</summary>
[Collection(PositionCostCollection.Name)]
public sealed class BrokerTransferTests(ApiFactory factory)
{
    [Fact]
    public async Task Money_can_be_moved_from_a_broker_to_its_spending_account_but_not_spent_there()
    {
        var api = await factory.OwnerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Guid broker;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            var created = Account.Create("Broker with a card", AccountKind.Broker, "EUR", 0m,
                DateOnly.FromDateTime(DateTime.UtcNow), "Trading 212");
            finance.Accounts.Add(created);
            await finance.SaveChangesAsync(CancellationToken.None);
            broker = created.Id;
        }

        var spending = await api.CreateAsync("/api/accounts", new
        {
            name = "Card spending pot", kind = "Bank", currency = "EUR", openingBalance = 0m, openingBalanceOn = today,
        });

        await api.CreateAsync("/api/transactions", new
        {
            type = "Transfer", occurredOn = today, amount = 80m, currency = "EUR", accountId = broker,
            counterAccountId = spending, description = "Top up the card",
        });
        var expense = await api.PostAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = today, amount = 10m, currency = "EUR", accountId = broker,
            description = "Streaming",
        });

        expense.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var account = (await api.GetAsync<JsonElement[]>("/api/accounts"))
            .Single(a => a.GetProperty("id").GetGuid() == spending);
        account.GetProperty("balance").GetDecimal().ShouldBe(80m);
    }
}
