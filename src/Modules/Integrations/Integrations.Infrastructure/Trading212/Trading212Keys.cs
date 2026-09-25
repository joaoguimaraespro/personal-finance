using System.Globalization;

namespace Integrations.Infrastructure.Trading212;

/// <summary>
/// Natural keys shared by the API and the CSV export, so a CSV imported after an API sync (or vice versa) merges
/// with what is already there instead of double counting — and can add withholding tax the API does not report.
/// </summary>
internal static class Trading212Keys
{
    private static string Id(string? isin, string? ticker) => (isin ?? ticker ?? "unknown").ToUpperInvariant();

    private static string Num(decimal value) => decimal.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture);

    public static string ForTrade(string? isin, string? ticker, DateTimeOffset at, decimal quantity) =>
        $"t212:trade:{Id(isin, ticker)}:{at.UtcDateTime:yyyyMMddHHmmss}:{decimal.Round(quantity, 6).ToString("0.######", CultureInfo.InvariantCulture)}";

    public static string ForDividend(string? isin, string? ticker, DateOnly paidOn, decimal netAmount) =>
        $"t212:div:{Id(isin, ticker)}:{paidOn:yyyyMMdd}:{Num(Math.Abs(netAmount))}";

    public static string ForCash(string type, DateTimeOffset at, decimal amount) =>
        $"t212:cash:{type}:{at.UtcDateTime:yyyyMMddHHmmss}:{Num(Math.Abs(amount))}";
}
