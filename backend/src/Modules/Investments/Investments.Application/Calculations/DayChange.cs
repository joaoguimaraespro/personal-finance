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

    /// <summary>How far a stored 24-hour reference may be from exactly 24 hours before the current price.</summary>
    public static readonly TimeSpan Rolling24hTolerance = TimeSpan.FromHours(1);

    /// <summary>
    /// The reference price for a coin's rolling 24-hour change: the price stored 24 hours before the current
    /// price was quoted. Null when there is none or it belongs to another quote (e.g. the latest refresh could not
    /// fetch it) — the previous close is used then.
    /// </summary>
    public static decimal? Reference24h(decimal? referencePrice, DateTimeOffset? referenceAtUtc,
        DateTimeOffset priceAsOfUtc) =>
        referencePrice is > 0 && referenceAtUtc is { } at &&
        (priceAsOfUtc - TimeSpan.FromHours(24) - at).Duration() <= Rolling24hTolerance
            ? referencePrice
            : null;

    /// <summary>The basis of a total: the same as all its parts, else mixed (nothing held: since the previous close).</summary>
    public static DayChangeBasis Combine(IEnumerable<DayChangeBasis> parts)
    {
        var distinct = parts.Distinct().ToList();
        return distinct.Count switch
        {
            0 => DayChangeBasis.PreviousClose,
            1 => distinct[0],
            _ => DayChangeBasis.Mixed,
        };
    }
}

/// <summary>What a day change compares the current price with.</summary>
public enum DayChangeBasis
{
    /// <summary>The previous trading day's close ("today").</summary>
    PreviousClose,

    /// <summary>The price 24 hours ago (coins, quoted around the clock, as exchanges show it).</summary>
    Rolling24Hours,

    /// <summary>A total of both kinds (shares since the previous close, coins over 24 hours).</summary>
    Mixed,
}
