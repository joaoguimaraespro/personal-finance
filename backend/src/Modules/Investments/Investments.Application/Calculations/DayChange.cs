namespace Investments.Application.Calculations;

/// <summary>
/// Change since the previous close, from the prices recorded at each sync. Brokers don't report a previous
/// close, so the last price recorded on an earlier day stands in for it: after a weekend the change is
/// Friday's move, as broker apps show it.
/// </summary>
public static class DayChange
{
    /// <summary>Latest recorded close strictly before the day of the current price, if any.</summary>
    public static decimal? PreviousClose(IEnumerable<(DateOnly Date, decimal Close)> closes, DateOnly priceDate) =>
        closes.Where(c => c.Date < priceDate && c.Close > 0)
            .OrderByDescending(c => c.Date)
            .Select(c => (decimal?)c.Close)
            .FirstOrDefault();

    /// <summary>
    /// The trading day a price belongs to. Exchanges are closed at weekends, but syncs still record the
    /// unchanged Friday price on Saturday and Sunday; comparing those with each other would show no change all
    /// weekend. Like broker apps, a weekend price counts as Friday's, so the weekend shows Friday's move.
    /// Crypto trades every day and keeps its own date.
    /// </summary>
    public static DateOnly TradingDay(DateOnly priceDate, bool tradesEveryDay) =>
        tradesEveryDay ? priceDate : priceDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => priceDate.AddDays(-1),
            DayOfWeek.Sunday => priceDate.AddDays(-2),
            _ => priceDate,
        };

    /// <summary>EUR change of a holding; null without a previous close.</summary>
    public static decimal? Amount(decimal quantity, decimal lastPrice, decimal? previousClose, decimal eurPerUnit) =>
        previousClose is { } prev ? decimal.Round(quantity * (lastPrice - prev) * eurPerUnit, 2) : null;

    /// <summary>Change relative to yesterday's value (today's value minus the change).</summary>
    public static decimal? Percent(decimal? change, decimal marketValue) =>
        change is { } c && marketValue - c != 0 ? decimal.Round(c / (marketValue - c), 6) : null;
}
