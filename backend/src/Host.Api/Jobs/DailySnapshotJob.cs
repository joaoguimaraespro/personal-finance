using Investments.Application.Manual;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;

namespace Host.Api.Jobs;

/// <summary>Records today's portfolio and net-worth values so history accrues. Idempotent within a day.</summary>
internal sealed class DailySnapshotJob(IServiceScopeFactory scopes, ILogger<DailySnapshotJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations, first requests) settle before the first run.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                // Coins entered by hand have no sync: fetch their price before valuing the day.
                await scope.ServiceProvider.GetRequiredService<ManualHoldingService>()
                    .RefreshPricesAsync(ManualHoldingService.PriceMaxAge, stoppingToken);
                await scope.ServiceProvider.GetRequiredService<PortfolioSnapshotter>().SnapshotTodayAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<NetWorthService>().SnapshotAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily snapshot failed");
            }

            try
            {
                // Catch-up for reconstructed history (cheap once prices are cached: no network).
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<HistoryReconstructor>().RebuildAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "History reconstruction failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
