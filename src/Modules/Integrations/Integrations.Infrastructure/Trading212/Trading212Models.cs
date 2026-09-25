using System.Text.Json.Serialization;

namespace Integrations.Infrastructure.Trading212;

// Shapes from the official Public API v0 reference (docs.trading212.com). Internal: they never leave this folder.

internal sealed record T212Page<T>(List<T> Items, string? NextPagePath);

internal sealed record T212Instrument(string? Ticker, string? Isin, string? Name, string? Currency);

internal sealed record T212Summary(long Id, string Currency, decimal TotalValue, T212Cash Cash, T212Investments Investments);

internal sealed record T212Cash(decimal AvailableToTrade, decimal InPies, decimal ReservedForOrders);

internal sealed record T212Investments(decimal CurrentValue, decimal TotalCost, decimal RealizedProfitLoss,
    decimal UnrealizedProfitLoss);

internal sealed record T212Position(decimal Quantity, decimal AveragePricePaid, decimal CurrentPrice,
    DateTimeOffset? CreatedAt, T212Instrument Instrument);

internal sealed record T212InstrumentMeta(string Ticker, string? Isin, string? CurrencyCode, string? Name,
    string? ShortName, string? Type);

internal sealed record T212HistoricalOrder(T212Order Order, T212Fill? Fill);

internal sealed record T212Order(long Id, string? Side, string? Currency, string? Ticker, T212Instrument? Instrument,
    string? Status);

internal sealed record T212Fill(long Id, decimal Price, decimal Quantity, DateTimeOffset FilledAt, string? Type,
    T212WalletImpact? WalletImpact);

internal sealed record T212WalletImpact(string? Currency, decimal? FxRate, decimal? NetValue,
    [property: JsonPropertyName("realisedProfitLoss")] decimal? RealisedProfitLoss, List<T212Tax>? Taxes);

internal sealed record T212Tax(string? Name, decimal Quantity, string? Currency);

internal sealed record T212Dividend(decimal Amount, decimal? AmountInEuro, string? Currency, decimal? GrossAmountPerShare,
    T212Instrument? Instrument, DateTimeOffset PaidOn, decimal? Quantity, string? Reference, string? Ticker,
    string? TickerCurrency, string? Type);

internal sealed record T212Transaction(decimal Amount, string? Currency, DateTimeOffset DateTime, string? Reference,
    string? Type);
