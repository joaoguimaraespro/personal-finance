using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Investments.Application.Abstractions;

/// <summary>
/// Persistence for broker-sourced data. Only the sync writer and background jobs write through it; HTTP
/// endpoints for investments are read-only (apart from user preferences such as target allocation).
/// </summary>
public interface IInvestmentsDb
{
    DbSet<Security> Securities { get; }
    DbSet<BrokerSymbol> BrokerSymbols { get; }
    DbSet<Position> Positions { get; }
    DbSet<Trade> Trades { get; }
    DbSet<Dividend> Dividends { get; }
    DbSet<CashMovement> CashMovements { get; }
    DbSet<CashBalance> CashBalances { get; }
    DbSet<MarketPrice> MarketPrices { get; }
    DbSet<FxRate> FxRates { get; }
    DbSet<PortfolioSnapshot> PortfolioSnapshots { get; }
    DbSet<TargetAllocation> TargetAllocations { get; }
    DbSet<ManualAsset> ManualAssets { get; }
    DbSet<NetWorthSnapshot> NetWorthSnapshots { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Source of reference exchange rates (ECB). Quotes are units per 1 EUR.</summary>
public interface IFxRateSource
{
    Task<IReadOnlyList<FxRate>> FetchAsync(FxHistory history, CancellationToken ct);
}

public enum FxHistory
{
    Latest = 0,
    Last90Days = 1,
    Full = 2,
}

/// <summary>
/// Optional external price source. The default implementation uses only prices reported by brokers, so no
/// market-data service is contacted unless one is configured.
/// </summary>
public interface IMarketDataProvider
{
    string Name { get; }

    Task<IReadOnlyList<(DateOnly Date, decimal Close)>> GetDailyClosesAsync(Security security, DateOnly from,
        DateOnly to, CancellationToken ct);
}
