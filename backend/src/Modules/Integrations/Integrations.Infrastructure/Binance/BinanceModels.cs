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

internal sealed record BinanceFiatPage<T>(List<T>? Data, int Total);

/// <summary>A fiat deposit (transactionType 0) or withdrawal (1): <c>indicatedAmount</c> is what was sent.</summary>
internal sealed record BinanceFiatOrder(string OrderNo, string FiatCurrency, decimal IndicatedAmount, decimal Amount,
    string? Status, string? Method, long CreateTime);

/// <summary>
/// Crypto bought directly with fiat (card, bank): <c>sourceAmount</c> of <c>fiatCurrency</c> paid in for
/// <c>obtainAmount</c> of <c>cryptoCurrency</c>; <c>totalFee</c> (in the fiat currency) is part of what was paid.
/// </summary>
internal sealed record BinanceFiatPayment(string OrderNo, string FiatCurrency, decimal SourceAmount,
    string? CryptoCurrency, string? Status, long CreateTime, decimal ObtainAmount = 0, decimal TotalFee = 0);

/// <summary>A Convert order: <c>fromAmount</c> of <c>fromAsset</c> swapped for <c>toAmount</c> of <c>toAsset</c>.</summary>
internal sealed record BinanceConversion(string? OrderStatus, string FromAsset, decimal FromAmount, string ToAsset,
    decimal ToAmount, long CreateTime);

internal sealed record BinanceConversionPage(List<BinanceConversion>? List, bool MoreData);

/// <summary>Coins received from outside Binance (status 1 = credited); <c>insertTime</c> in ms.</summary>
internal sealed record BinanceCoinReceived(string Coin, decimal Amount, int Status, long InsertTime);

/// <summary>Coins sent out of Binance (status 6 = completed); <c>applyTime</c> is "yyyy-MM-dd HH:mm:ss" UTC.</summary>
internal sealed record BinanceCoinSent(string Coin, decimal Amount, decimal TransactionFee, int Status, string ApplyTime);
