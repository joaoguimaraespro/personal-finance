using Ai.Application.Gateway;

namespace Host.Api.Jobs;

/// <summary>Every six hours: permanently removes what AI clients deleted more than 30 days ago (ADR-0008).</summary>
internal sealed class AiRecycleBinPurgeJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<AiRecycleBinPurgeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AiRecycleBin>().PurgeAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AI recycle bin purge failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
