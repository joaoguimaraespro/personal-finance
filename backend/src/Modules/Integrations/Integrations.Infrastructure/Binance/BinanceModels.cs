namespace Integrations.Infrastructure.Binance;

// Binance sends decimals as strings; the client reads them with AllowReadingFromString.

internal sealed record BinanceApiRestrictions(
    bool EnableReading,
    bool EnableSpotAndMarginTrading,
    bool EnableWithdrawals,
    bool EnableInternalTransfer,
    bool EnableMargin,
    bool EnableFutures,
    bool PermitsUniversalTransfer,
    bool EnableVanillaOptions,
    bool EnablePortfolioMarginTrading,
    bool EnableFixApiTrade,
    bool EnableFixReadOnly,
    bool IpRestrict);

internal sealed record BinanceBalance(string Asset, decimal Free, decimal Locked);

internal sealed record BinanceAccount(List<BinanceBalance> Balances);

internal sealed record BinancePage<T>(List<T>? Rows, int Total);

internal sealed record BinanceFlexiblePosition(string Asset, decimal TotalAmount);

internal sealed record BinanceLockedPosition(string Asset, decimal Amount);

/// <summary>A flexible-product reward: <c>rewards</c> of <c>asset</c> at <c>time</c> (ms).</summary>
internal sealed record BinanceFlexibleReward(string Asset, decimal Rewards, string? Type, long Time);

/// <summary>A locked-product reward: <c>amount</c> of <c>asset</c> at <c>time</c> (ms).</summary>
internal sealed record BinanceLockedReward(string Asset, decimal Amount, string? Type, long Time, long? PositionId);

internal sealed record BinanceTicker(string Symbol, decimal Price);

/// <summary>An executed fill of the account on one symbol (GET /api/v3/myTrades).</summary>
internal sealed record BinanceFill(long Id, decimal Price, decimal Qty, decimal QuoteQty, long Time, bool IsBuyer);

internal sealed record BinanceError(int Code, string? Msg);
