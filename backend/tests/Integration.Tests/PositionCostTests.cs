using Integration.Tests.Infrastructure;
using Investments.Application.Abstractions;
using Investments.Application.Sync;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace Integration.Tests;

[CollectionDefinition(Name)]
public sealed class PositionCostCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "position-cost";
}

/// <summary>A position whose cost the broker cannot tell keeps the cost of its first sync, so its gain shows.</summary>
[Collection(PositionCostCollection.Name)]
public sealed class PositionCostTests(ApiFactory factory)
{
    [Fact]
    public async Task An_unknown_cost_keeps_the_first_synced_price_and_a_known_cost_replaces_it()
    {
        var accountId = Guid.NewGuid();
        var coin = new SecurityReport("ZZCOIN", null, "ZZCOIN", null, "ZZCOIN", "EUR", AssetClass.Crypto);
        var at = DateTimeOffset.UtcNow;

        await SyncAsync(new PositionReport(coin, 2, 100m, 100m, at, CostKnown: false));
        (await PositionAsync()).AveragePrice.ShouldBe(100m);

        // Price fell: the cost stays, so the loss shows instead of a 0 % gain.
        await SyncAsync(new PositionReport(coin, 2, 80m, 80m, at, CostKnown: false));
        var fallen = await PositionAsync();
        fallen.AveragePrice.ShouldBe(100m);
        fallen.LastPrice.ShouldBe(80m);

        await SyncAsync(new PositionReport(coin, 2, 90m, 80m, at));
        (await PositionAsync()).AveragePrice.ShouldBe(90m);

        async Task SyncAsync(PositionReport report)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PortfolioSyncWriter>()
                .ReplacePositionsAsync(accountId, DataSource.Binance, [report], CancellationToken.None);
        }

        async Task<Position> PositionAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInvestmentsDb>().Positions.AsNoTracking()
                .SingleAsync(p => p.AccountId == accountId);
        }
    }
}
