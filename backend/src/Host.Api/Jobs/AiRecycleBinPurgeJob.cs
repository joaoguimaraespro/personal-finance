using Ai.Application.Gateway;

namespace Host.Api.Jobs;

/// <summary>Every six hours: permanently removes what AI clients deleted more than 30 days ago (ADR-0008).</summary>
internal sealed class AiRecycleBinPurgeJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<AiRecycleBinPurgeJob> logger) : WallClockJob(clock, logger)
{
    private readonly TimeProvider _clock = clock;

    protected override TimeSpan StartupDelay => TimeSpan.Zero;

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AiRecycleBin>().PurgeAsync(_clock.GetUtcNow(), ct);
    }
}
