using Finance.Application.Interest;

namespace Host.Api.Jobs;

/// <summary>
/// Hourly: brings every savings account's estimated interest up to today (a new day adds a day of accrual) and picks
/// up ledger changes made outside the transaction endpoints (imports, syncs). Idempotent.
/// </summary>
internal sealed class InterestAccrualJob(IServiceScopeFactory scopes, ILogger<InterestAccrualJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup (migrations, first requests) settle before the first run.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<InterestAccrualService>().RecalculateAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Interest accrual run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
