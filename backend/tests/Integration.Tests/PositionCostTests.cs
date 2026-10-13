using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Integration.Tests.Infrastructure;
using Investments.Application.Abstractions;
using Investments.Application.Prices;
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

    [Fact]
    public async Task A_crypto_wallets_return_is_its_coins_gain_over_their_cost()
    {
        var api = await factory.OwnerAsync();
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            var account = Account.Create("Binance", AccountKind.Broker, "EUR", 0, DateOnly.FromDateTime(DateTime.UtcNow),
                "Binance");
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(CancellationToken.None);
            accountId = account.Id;
        }

        var coin = new SecurityReport("YYCOIN", null, "YYCOIN", null, "YYCOIN", "EUR", AssetClass.Crypto);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<PortfolioSyncWriter>();
            await writer.ReplacePositionsAsync(accountId, DataSource.Binance,
                [new PositionReport(coin, 2, 100m, 150m, DateTimeOffset.UtcNow)], CancellationToken.None);
            // Only a small, recent fiat deposit is known: most coins were sent in, so it is not what they cost.
            await writer.AddCashMovementsAsync(accountId, DataSource.Binance,
                [new CashMovementReport("binance:fiat:deposit:T1", CashMovementType.Deposit, 50m, "EUR",
                    DateTimeOffset.UtcNow.AddDays(-3), "Deposit")], CancellationToken.None);
        }

        var summary = await api.GetJsonAsync("/api/portfolio/summary");
        var wallet = summary.GetProperty("accounts").EnumerateArray()
            .Single(a => a.GetProperty("accountId").GetGuid() == accountId);

        wallet.GetProperty("totalReturn").GetDecimal().ShouldBe(100m); // 2 × (150 − 100), not 300 − 50
        wallet.GetProperty("totalReturnPercent").GetDecimal().ShouldBe(0.5m);
        wallet.GetProperty("periodReturn").GetProperty("percent").GetDecimal().ShouldBe(0.5m);
        wallet.GetProperty("periodReturn").GetProperty("costBased").GetBoolean().ShouldBeTrue();
        wallet.GetProperty("periodReturn").GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        summary.GetProperty("totalReturn").GetDecimal().ShouldBe(
            summary.GetProperty("accounts").EnumerateArray().Sum(a => a.GetProperty("totalReturn").GetDecimal()));
        var scoped = await api.GetJsonAsync($"/api/portfolio/summary?accountId={accountId}");
        scoped.GetProperty("totalReturnPercent").GetDecimal().ShouldBe(0.5m);
        scoped.GetProperty("periodReturn").ValueKind.ShouldBe(JsonValueKind.Object);
        scoped.GetProperty("periodReturn").GetProperty("costBased").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_end_of_day_brokers_positions_follow_the_public_price_between_syncs()
    {
        factory.Prices.ClosesByIsin["IE00REPRICE1"] = 123m;
        var accountId = Guid.NewGuid();
        var fund = new SecurityReport("RPX", "IE00REPRICE1", "RPX", "XETRA", "Reprice fund", "EUR", AssetClass.Etf);
        var lastClose = DateTimeOffset.UtcNow.AddDays(-7);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioSyncWriter>().ReplacePositionsAsync(accountId,
                DataSource.InteractiveBrokers, [new PositionReport(fund, 10, 90m, 100m, lastClose)],
                CancellationToken.None);
        }

        Position before;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            before = await scope.ServiceProvider.GetRequiredService<IInvestmentsDb>().Positions.AsNoTracking()
                .SingleAsync(p => p.AccountId == accountId);
            (await scope.ServiceProvider.GetRequiredService<IntradayRepricer>()
                .RepriceAsync(TimeSpan.FromMinutes(15), CancellationToken.None)).ShouldBeGreaterThan(0);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var after = await scope.ServiceProvider.GetRequiredService<IInvestmentsDb>().Positions.AsNoTracking()
                .SingleAsync(p => p.AccountId == accountId);
            after.LastPrice.ShouldBe(123m);
            after.PriceAsOfUtc.ShouldBeGreaterThan(lastClose);
            after.Quantity.ShouldBe(10m); // quantity, cost and the sync time still come from IBKR
            after.AveragePrice.ShouldBe(90m);
            after.SyncedAtUtc.ShouldBe(before.SyncedAtUtc);
        }
    }

    [Fact]
    public async Task A_holding_costs_what_its_trades_cost_in_euros_when_they_explain_it()
    {
        var api = await factory.OwnerAsync();
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<IFinanceDb>();
            var account = Account.Create("Stocks", AccountKind.Broker, "EUR", 0m, DateOnly.FromDateTime(DateTime.UtcNow),
                "Trading 212");
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(CancellationToken.None);
            accountId = account.Id;
        }

        var explained = new SecurityReport("BOOK1", null, "BOOK1", null, "Book one", "EUR", AssetClass.Stock);
        var partial = new SecurityReport("BOOK2", null, "BOOK2", null, "Book two", "EUR", AssetClass.Stock);
        var at = DateTimeOffset.UtcNow.AddDays(-30);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<PortfolioSyncWriter>();
            // The broker says 100 a share; the two purchases took 1,050 EUR out of the account (fees, conversion).
            await writer.ReplacePositionsAsync(accountId, DataSource.Trading212,
                [new PositionReport(explained, 10, 100m, 120m, DateTimeOffset.UtcNow),
                 new PositionReport(partial, 10, 50m, 60m, DateTimeOffset.UtcNow)], CancellationToken.None);
            await writer.AddTradesAsync(accountId, DataSource.Trading212,
            [
                new TradeReport("book:1", explained, TradeSide.Buy, 6, 100m, "EUR", 2m, 0m, "EUR", -640m, "EUR", null, at),
                new TradeReport("book:2", explained, TradeSide.Buy, 4, 100m, "EUR", 2m, 0m, "EUR", -410m, "EUR", null,
                    at.AddDays(1)),
                // Only 5 of the 10 held: the trades do not explain the holding, so the broker's price stays.
                new TradeReport("book:3", partial, TradeSide.Buy, 5, 40m, "EUR", 0m, 0m, "EUR", -200m, "EUR", null, at),
            ], CancellationToken.None);
        }

        var positions = await api.GetAsync<JsonElement[]>($"/api/portfolio/positions?accountId={accountId}");
        positions.Single(p => p.GetProperty("symbol").GetString() == "BOOK1").GetProperty("costBase").GetDecimal()
            .ShouldBe(1050m);
        positions.Single(p => p.GetProperty("symbol").GetString() == "BOOK2").GetProperty("costBase").GetDecimal()
            .ShouldBe(500m);
    }
}
