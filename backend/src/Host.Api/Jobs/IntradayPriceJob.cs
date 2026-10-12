using Investments.Application.Manual;
using Investments.Application.Prices;

namespace Host.Api.Jobs;

/// <summary>
/// Every 30 minutes: current public prices for coins entered by hand and for end-of-day brokers' positions (IBKR),
/// so the portfolio, dashboard and net worth move during the day without opening the portfolio page.
/// </summary>
internal sealed class IntradayPriceJob(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<IntradayPriceJob> logger) : WallClockJob(clock, logger)
{
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(3);

    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ManualHoldingService>()
            .RefreshPricesAsync(ManualHoldingService.PriceMaxAge, ct);
        await scope.ServiceProvider.GetRequiredService<IntradayRepricer>()
            .RepriceAsync(ManualHoldingService.PriceMaxAge, ct);
    }
}
