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
                await scope.ServiceProvider.GetRequiredService<PortfolioSnapshotter>().SnapshotTodayAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<NetWorthService>().SnapshotAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily snapshot failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
