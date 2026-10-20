using System.Text.Json;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[CollectionDefinition(Name)]
public sealed class TrackingStartCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "tracking-start";
}

/// <summary>
/// The month tracking started in, part-way through, is marked partial and stays out of the averages the next months
/// are compared with (its income only covers the days after the start).
/// </summary>
[Collection(TrackingStartCollection.Name)]
public sealed class TrackingStartTests(ApiFactory factory)
{
    [Fact]
    public async Task The_month_tracking_started_in_is_partial_and_left_out_of_the_averages()
    {
        var api = await factory.OwnerAsync();
        var bank = await api.CreateAsync("/api/accounts", new
        {
            name = "Started mid-month", kind = "Bank", currency = "EUR", openingBalance = 500m, openingBalanceOn = "2055-10-15",
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Income", occurredOn = "2055-10-20", amount = 0.77m, accountId = bank,
            categoryId = SystemCatalog.CategoryId("interest"),
        });
        await api.CreateAsync("/api/transactions", new
        {
            type = "Income", occurredOn = "2055-11-01", amount = 2400m, accountId = bank,
            categoryId = SystemCatalog.CategoryId("salary"),
        });

        var october = await api.GetJsonAsync("/api/reports/monthly/2055-10");
        october.GetProperty("partialFrom").GetString().ShouldBe("2055-10-15");

        var november = await api.GetJsonAsync("/api/reports/monthly/2055-11");
        november.GetProperty("partialFrom").ValueKind.ShouldBe(JsonValueKind.Null);
        november.GetProperty("trailingAverage").GetProperty("months").GetInt32().ShouldBe(0);
    }
}
