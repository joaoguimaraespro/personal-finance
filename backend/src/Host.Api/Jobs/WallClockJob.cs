namespace Host.Api.Jobs;

/// <summary>When a periodic job is due, judged by the wall clock rather than by elapsed process time.</summary>
internal static class JobSchedule
{
    /// <summary>
    /// Due on the first check, once <paramref name="interval"/> has passed since the last run, on a new UTC day (daily
    /// figures must be brought up to the new date at once), or when the clock moved backwards.
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastRun, DateTimeOffset now, TimeSpan interval) =>
        lastRun is not { } last
        || now < last
        || now - last >= interval
        || now.UtcDateTime.Date != last.UtcDateTime.Date;
}

/// <summary>
/// A periodic job for a host that is not always on (a laptop or desktop that sleeps, hibernates or is switched off).
/// Timers count running time, so a plain <see cref="PeriodicTimer"/> started before a sleep can fire hours late after
/// resume. Instead the job wakes every <see cref="PollInterval"/> and runs when <see cref="JobSchedule.IsDue"/> says the
/// wall clock has moved far enough — at most one poll late after any sleep, and right after start-up (following
/// <see cref="StartupDelay"/>) when the host was off. Runs are idempotent, so running early never duplicates work.
/// </summary>
internal abstract class WallClockJob(TimeProvider clock, ILogger logger) : BackgroundService
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(1);

    /// <summary>Lets start-up (migrations, first requests) settle before the first run.</summary>
    protected abstract TimeSpan StartupDelay { get; }

    /// <summary>Normal spacing between runs while the host stays up.</summary>
    protected abstract TimeSpan Interval { get; }

    protected virtual TimeSpan PollInterval => DefaultPollInterval;

    protected abstract Task RunOnceAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (StartupDelay > TimeSpan.Zero)
        {
            await Task.Delay(StartupDelay, clock, stoppingToken);
        }

        DateTimeOffset? lastRun = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            if (JobSchedule.IsDue(lastRun, now, Interval))
            {
                lastRun = now;
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Job} run failed", GetType().Name);
                }
            }

            await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }
}
