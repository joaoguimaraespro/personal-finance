using Integrations.Application;
using Integrations.Application.Connections;
using Integrations.Application.Contracts;
using Integrations.Application.Sync;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Jobs;

/// <summary>
/// Runs broker syncs one at a time: manual requests from the queue, plus a schedule — Trading 212 every 4 hours,
/// IBKR once a day after its end-of-day processing. Connections needing attention wait for the user; failed
/// attempts back off (see <see cref="SyncSchedule"/>).
/// </summary>
internal sealed class BrokerSyncService(IServiceScopeFactory scopes, SyncQueue queue, TimeProvider clock,
    ILogger<BrokerSyncService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ProcessQueueAsync(stoppingToken), ScheduleAsync(stoppingToken));

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var (connectionId, trigger) in queue.ReadAllAsync(ct))
        {
            await RunAsync(connectionId, trigger, ct);
        }
    }

    private async Task ScheduleAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), clock, ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30), clock);
        do
        {
            List<BrokerConnection> due;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IIntegrationsDb>();
                var active = await db.Connections.AsNoTracking()
                    .Where(c => c.Status == ConnectionStatus.Active).ToListAsync(ct);
                var ids = active.Select(c => c.Id).ToList();
                var now = clock.GetUtcNow();
                // Only recent jobs matter for the back-off (the longest wait is 12 h).
                var since = now.AddDays(-2);
                var lastJobs = (await db.SyncJobs.AsNoTracking()
                        .Where(j => ids.Contains(j.ConnectionId) && j.FinishedAtUtc != null && j.StartedAtUtc >= since)
                        .ToListAsync(ct))
                    .GroupBy(j => j.ConnectionId)
                    .Select(g => g.MaxBy(j => j.StartedAtUtc)!)
                    .ToDictionary(j => j.ConnectionId, j => new LastAttempt(j.StartedAtUtc,
                        j.Outcome == SyncOutcome.Failed, SyncSchedule.IsLockout(j.Errors)));
                due = active.Where(c => SyncSchedule.IsDue(c.Kind, c.LastSuccessfulSyncUtc,
                    lastJobs.GetValueOrDefault(c.Id), now)).ToList();
            }

            foreach (var connection in due)
            {
                await RunAsync(connection.Id, SyncTrigger.Scheduled, ct);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunAsync(Guid connectionId, SyncTrigger trigger, CancellationToken ct)
    {
        await _oneAtATime.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SyncPipeline>().RunAsync(connectionId, trigger, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Broker sync crashed for {ConnectionId}", connectionId);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}
