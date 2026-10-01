namespace Investments.Application.Calculations;

/// <summary>Pure rules for coins entered by hand (valued with public prices, quoted every day of the week).</summary>
public static class ManualCrypto
{
    /// <summary>A close older than this is not used to value a reward.</summary>
    public const int StaleCloseDays = 7;

    /// <summary>
    /// EUR income of a reward: the quantity at the latest close on or before the day it was received (within a
    /// week), else at <paramref name="fallbackPrice"/> (the latest known price).
    /// </summary>
    public static decimal RewardValue(decimal quantity, DateOnly receivedOn,
        IEnumerable<(DateOnly Date, decimal Close)> closes, decimal fallbackPrice)
    {
        var close = closes
            .Where(c => c.Date <= receivedOn && c.Date >= receivedOn.AddDays(-StaleCloseDays) && c.Close > 0)
            .OrderByDescending(c => c.Date)
            .Select(c => (decimal?)c.Close)
            .FirstOrDefault();
        return decimal.Round(quantity * (close ?? fallbackPrice), 4);
    }

    /// <summary>
    /// When a close was observed: today's daily bar is the live price (as of now); an older bar is that day's final
    /// close (end of the UTC day — crypto days are UTC days).
    /// </summary>
    public static DateTimeOffset PriceAsOf(DateOnly closeDate, DateTimeOffset now) =>
        closeDate >= DateOnly.FromDateTime(now.UtcDateTime)
            ? now
            : new DateTimeOffset(closeDate.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
}
