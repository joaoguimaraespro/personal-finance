using Finance.Application.Interest;

namespace Host.Api.Jobs;

/// <summary>
/// Hourly, and as soon as a new day begins: brings every savings account's estimated interest up to today (a new day
/// adds a day of accrual; days the host was off are included, since each run recalculates from daily balances) and
/// picks up ledger changes made outside the transaction endpoints (imports, syncs). Idempotent.
/// </summary>
internal sealed class InterestAccrualJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<InterestAccrualJob> logger) : WallClockJob(clock, logger)
{
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(1);

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InterestAccrualService>().RecalculateAllAsync(ct);
    }
}
