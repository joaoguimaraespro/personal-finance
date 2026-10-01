using System.Text.Json;
using Integration.Tests.Infrastructure;
using Investments.Application.Abstractions;
using Investments.Application.Portfolio;
using Investments.Application.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

[CollectionDefinition(Name)]
public sealed class HistoryCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "history";
}

/// <summary>
/// A broker without valuation history (demo profile "c", like Trading 212) gets a daily history rebuilt from its
/// ledger and public closes — on its own database so the other portfolio tests keep their two demo accounts.
/// </summary>
[Collection(HistoryCollection.Name)]
public sealed class HistoryReconstructionTests(ApiFactory factory)
{
    [Fact]
    public async Task Account_without_broker_history_gets_a_reconstructed_daily_series()
    {
        var api = await factory.OwnerAsync();
        var connectionId = await api.CreateAsync("/api/integrations/connections", new
        {
            kind = "Demo", displayName = "Trading 212-like", credentials = new Dictionary<string, string> { ["profile"] = "c" },
        });
        var connection = await WaitForSyncAsync(api, connectionId);
        var accountId = connection.GetProperty("accountId").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // The rebuild runs in the background after the sync.
        var performance = await PollAsync(api, $"/api/portfolio/performance?accountId={accountId}",
            p => p.GetProperty("reconstructedBefore").ValueKind == JsonValueKind.String);

        performance.GetProperty("reconstructedBefore").GetString().ShouldBe(today.ToString("yyyy-MM-dd"));
        performance.GetProperty("estimatedDays").GetInt32().ShouldBe(0);
        var series = performance.GetProperty("series").EnumerateArray().ToList();
        series.Count.ShouldBeGreaterThan(100);
        DateOnly.Parse(series[0].GetProperty("date").GetString()!).ShouldBeLessThan(today.AddMonths(-20));
        series[^1].GetProperty("date").GetString().ShouldBe(today.ToString("yyyy-MM-dd"));

        // Contributions step up with each monthly deposit instead of being a flat line.
        series[0].GetProperty("netContributions").GetDecimal().ShouldBe(600m);
        series[^1].GetProperty("netContributions").GetDecimal().ShouldBeGreaterThan(10_000m);

        // No jump on the first real day beyond the price difference (fake closes vs the broker's prices).
        var lastReconstructed = series[^2].GetProperty("value").GetDecimal();
        var firstReal = series[^1].GetProperty("value").GetDecimal();
        (Math.Abs(firstReal - lastReconstructed) / firstReal).ShouldBeLessThan(0.1m);
        performance.GetProperty("timeWeightedReturn").ValueKind.ShouldBe(JsonValueKind.Number);
        performance.GetProperty("moneyWeightedReturn").ValueKind.ShouldBe(JsonValueKind.Number);

        // Rebuilding is idempotent and needs no new downloads: each day is fetched once.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvestmentsDb>();
        var reconstructed = await db.PortfolioSnapshots
            .CountAsync(s => s.AccountId == accountId && s.Origin == SnapshotOrigins.Reconstructed);
        var requests = factory.Prices.Requests.Count;

        var written = await scope.ServiceProvider.GetRequiredService<HistoryReconstructor>()
            .RebuildAsync(accountId, CancellationToken.None);

        written.ShouldBe(reconstructed);
        factory.Prices.Requests.Count.ShouldBe(requests);
        (await db.PortfolioSnapshots.CountAsync(s => s.AccountId == accountId && s.Date == today)).ShouldBe(1);
        (await db.PortfolioSnapshots.SingleAsync(s => s.AccountId == accountId && s.Date == today)).Origin
            .ShouldBe(SnapshotOrigins.Computed);
        (await db.MarketPrices.CountAsync(p => p.Source == SharedKernel.DataSource.MarketData)).ShouldBeGreaterThan(400);
    }

    private static async Task<JsonElement> WaitForSyncAsync(ApiClient api, Guid connectionId)
    {
        for (var i = 0; i < 120; i++)
        {
            var connections = await api.GetAsync<JsonElement[]>("/api/integrations/connections");
            var connection = connections.Single(c => c.GetProperty("id").GetGuid() == connectionId);
            if (connection.GetProperty("lastSuccessfulSyncUtc").ValueKind == JsonValueKind.String)
            {
                return connection;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Sync did not finish.");
    }

    private static async Task<JsonElement> PollAsync(ApiClient api, string url, Func<JsonElement, bool> done)
    {
        for (var i = 0; i < 120; i++)
        {
            var body = await api.GetJsonAsync(url);
            if (done(body))
            {
                return body;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{url} never satisfied the condition.");
    }
}
