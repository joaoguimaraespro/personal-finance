namespace Integrations.Infrastructure.Binance;

/// <summary>Pure rules for turning Binance balances, fills and rewards into EUR figures.</summary>
internal static class BinanceValuation
{
    /// <summary>Quotes whose fills count towards a coin's cost: EUR and the dollar stablecoins (≈ USD).</summary>
    public static readonly string[] CostQuotes = ["EUR", "USDT", "USDC", "FDUSD"];

    private static readonly HashSet<string> UsdStable = new(StringComparer.Ordinal) { "USDT", "USDC", "FDUSD" };

    /// <summary>
    /// Flexible Earn used to appear in spot balances as "LD" + asset; those rows are the Earn position reported by
    /// the Simple Earn endpoints, so they are dropped to avoid counting it twice.
    /// </summary>
    public static bool IsEarnShadow(string asset, IReadOnlySet<string> earnAssets) =>
        asset.Length > 2 && asset.StartsWith("LD", StringComparison.Ordinal) && earnAssets.Contains(asset[2..]);

    /// <summary>
    /// Price of one <paramref name="asset"/> in EUR from the price list: the EUR pair, else a dollar stablecoin pair
    /// converted with EURUSDT (dollars per euro). Null when Binance quotes neither.
    /// </summary>
    public static decimal? EurPrice(string asset, IReadOnlyDictionary<string, decimal> prices)
    {
        if (asset == "EUR")
        {
            return 1m;
        }

        if (prices.TryGetValue(asset + "EUR", out var eur) && eur > 0)
        {
            return eur;
        }

        if (!prices.TryGetValue("EURUSDT", out var usdPerEur) || usdPerEur <= 0)
        {
            return null;
        }

        if (UsdStable.Contains(asset))
        {
            return Round(1m / usdPerEur);
        }

        foreach (var quote in UsdStable)
        {
            if (prices.TryGetValue(asset + quote, out var usd) && usd > 0)
            {
                return Round(usd / usdPerEur);
            }
        }

        return null;
    }

    /// <summary>
    /// Average EUR cost of the coins bought, from fills in EUR or dollar stablecoins (moving average: sells reduce the
    /// quantity, not the average). Dollar fills are converted with that day's EURUSDT close (else the latest one).
    /// Null when nothing was bought on these pairs — coins deposited, converted or earned have no known cost.
    /// </summary>
    public static decimal? AverageCost(IEnumerable<(string Quote, BinanceFill Fill)> fills,
        IReadOnlyDictionary<DateOnly, decimal> usdPerEurCloses, decimal latestUsdPerEur)
    {
        decimal quantity = 0, cost = 0;
        foreach (var (quote, f) in fills.OrderBy(x => x.Fill.Time))
        {
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(f.Time).UtcDateTime);
            var toEur = quote == "EUR" ? 1m : 1m / UsdPerEurOn(day, usdPerEurCloses, latestUsdPerEur);
            if (f.IsBuyer)
            {
                quantity += f.Qty;
                cost += f.QuoteQty * toEur;
            }
            else if (quantity > 0)
            {
                var sold = Math.Min(f.Qty, quantity);
                cost -= cost / quantity * sold;
                quantity -= sold;
            }
        }

        return quantity > 0 ? Round(cost / quantity) : null;
    }

    /// <summary>EUR value of a reward: quantity × that day's close in EUR (else the current price).</summary>
    public static decimal RewardValue(decimal quantity, DateOnly day, IReadOnlyDictionary<DateOnly, decimal> eurCloses,
        decimal currentEurPrice)
    {
        var close = eurCloses.TryGetValue(day, out var c) && c > 0
            ? c
            : eurCloses.Where(x => x.Key <= day && x.Key >= day.AddDays(-7) && x.Value > 0)
                .OrderByDescending(x => x.Key).Select(x => (decimal?)x.Value).FirstOrDefault() ?? currentEurPrice;
        return decimal.Round(quantity * close, 4);
    }

    private static decimal UsdPerEurOn(DateOnly day, IReadOnlyDictionary<DateOnly, decimal> closes, decimal latest) =>
        closes.TryGetValue(day, out var v) && v > 0 ? v : latest;

    private static decimal Round(decimal value) => decimal.Round(value, 8);
}
