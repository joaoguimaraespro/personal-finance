namespace SharedKernel;

/// <summary>
/// Provenance of every financial record, so the user can always audit where a number came from.
/// </summary>
public enum DataSource
{
    Manual = 0,
    Recurring = 1,
    Xlsx = 2,
    Csv = 3,
    Json = 4,
    Trading212 = 10,
    InteractiveBrokers = 11,
    Binance = 12,

    /// <summary>Public end-of-day closing prices from the configured market-data provider (never account data).</summary>
    MarketData = 20,

    /// <summary>
    /// Interest estimated by the app from a savings account's rate (TANB) and daily balances. Recalculated
    /// automatically and never edited by hand; replaced by a real entry once the user reconciles the month.
    /// </summary>
    InterestEstimate = 30,

    /// <summary>Fictitious data for demos and screenshots. Only available when explicitly enabled.</summary>
    Demo = 99,
}
