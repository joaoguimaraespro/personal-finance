using Investments.Application.Manual;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;

namespace Host.Api.Jobs;

/// <summary>
/// Records today's portfolio and net-worth values so history accrues: shortly after start-up, every six hours and as
/// soon as a new day begins. Idempotent within a day. Days the host was off get no computed snapshot of their own;
/// performance maths attributes deposits to the interval between snapshots, so returns stay correct across the gap.
/// </summary>
internal sealed class DailySnapshotJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DailySnapshotJob> logger)
    : WallClockJob(clock, logger)
{
    private readonly ILogger<DailySnapshotJob> _logger = logger;

    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(1);

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            // Coins entered by hand have no sync: fetch their price before valuing the day.
            await scope.ServiceProvider.GetRequiredService<ManualHoldingService>()
                .RefreshPricesAsync(ManualHoldingService.PriceMaxAge, ct);
            await scope.ServiceProvider.GetRequiredService<PortfolioSnapshotter>().SnapshotTodayAsync(ct);
            await scope.ServiceProvider.GetRequiredService<NetWorthService>().SnapshotAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Daily snapshot failed");
        }

        try
        {
            // Catch-up for reconstructed history (cheap once prices are cached: no network).
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HistoryReconstructor>().RebuildAllAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "History reconstruction failed");
        }
    }
}
