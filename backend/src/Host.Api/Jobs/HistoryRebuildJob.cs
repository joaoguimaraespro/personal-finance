using Investments.Application.Portfolio;

namespace Host.Api.Jobs;

/// <summary>
/// Rebuilds the reconstructed valuation history of accounts queued after a sync or CSV import, one at a time.
/// Failures (e.g. the price provider being down) are logged; the next sync or the daily job tries again.
/// </summary>
internal sealed class HistoryRebuildJob(IServiceScopeFactory scopes, HistoryRebuildQueue queue,
    ILogger<HistoryRebuildJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var accountId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<HistoryReconstructor>()
                    .RebuildAsync(accountId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "History reconstruction failed for account {AccountId}", accountId);
            }
        }
    }
}
