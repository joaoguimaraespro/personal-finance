using Finance.Application.Recurring;

namespace Host.Api.Jobs;

/// <summary>Hourly: turns due recurring templates into pending proposals for the user to confirm.</summary>
internal sealed class RecurringProposalJob(IServiceScopeFactory scopes, ILogger<RecurringProposalJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var created = await scope.ServiceProvider.GetRequiredService<RecurringProposer>()
                    .ProposeDueAsync(stoppingToken);
                logger.LogInformation("Recurring proposals created: {Count}", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Recurring proposal run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
