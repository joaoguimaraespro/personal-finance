using Finance.Application.Recurring;

namespace Host.Api.Jobs;

/// <summary>
/// At start-up, hourly and as soon as a new day begins: turns due recurring templates into pending proposals for the
/// user to confirm. Occurrences that fell due while the host was off are all proposed (never twice).
/// </summary>
internal sealed class RecurringProposalJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<RecurringProposalJob> logger) : WallClockJob(clock, logger)
{
    private readonly ILogger<RecurringProposalJob> _logger = logger;

    protected override TimeSpan StartupDelay => TimeSpan.Zero;

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<RecurringProposer>().ProposeDueAsync(ct);
        _logger.LogInformation("Recurring proposals created: {Count}", created);
    }
}
