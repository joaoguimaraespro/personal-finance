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
    /// Average EUR cost of the coins bought on EUR and dollar-stablecoin pairs (see <see cref="AverageCost(IEnumerable{CostEvent})"/>).
    /// Dollar fills are converted with that day's EURUSDT close (else the latest one).
    /// </summary>
    public static decimal? AverageCost(IEnumerable<(string Quote, BinanceFill Fill)> fills,
        IReadOnlyDictionary<DateOnly, decimal> usdPerEurCloses, decimal latestUsdPerEur) =>
        AverageCost(FillEvents(fills, usdPerEurCloses, latestUsdPerEur));

    public static IEnumerable<CostEvent> FillEvents(IEnumerable<(string Quote, BinanceFill Fill)> fills,
        IReadOnlyDictionary<DateOnly, decimal> usdPerEurCloses, decimal latestUsdPerEur) =>
        fills.Select(x =>
        {
            var (quote, f) = x;
            if (!f.IsBuyer)
            {
                return CostEvent.Out(f.Time, f.Qty, "trade");
            }

            var day = DayOf(f.Time);
            var toEur = quote == "EUR" ? 1m : 1m / UsdPerEurOn(day, usdPerEurCloses, latestUsdPerEur);
            return CostEvent.In(f.Time, f.Qty, f.QuoteQty * toEur, "trade");
        });

    /// <summary>
    /// EUR cost per coin as Binance shows it ("cost price"): the average of everything that came in — bought, converted
    /// to, received, earned — over the account's whole history. Coins going out (sold, converted from, sent to another
    /// wallet) do not change it. Null when nothing came in: the cost of the coins held is unknown.
    /// </summary>
    public static decimal? AverageCost(IEnumerable<CostEvent> events)
    {
        decimal quantity = 0, cost = 0;
        foreach (var e in events.Where(e => e.Quantity > 0))
        {
            quantity += e.Quantity;
            cost += e.EurValue;
        }

        return quantity > 0 && cost > 0 ? Round(cost / quantity) : null;
    }

    /// <summary>Quantity and EUR cost left after the moving average has run over every event.</summary>
    public static (decimal Quantity, decimal Cost) Replay(IEnumerable<CostEvent> events)
    {
        decimal quantity = 0, cost = 0;
        foreach (var e in events.OrderBy(e => e.Time).ThenByDescending(e => e.Quantity))
        {
            if (e.Quantity > 0)
            {
                quantity += e.Quantity;
                cost += e.EurValue;
            }
            else if (quantity > 0)
            {
                var gone = Math.Min(-e.Quantity, quantity);
                cost -= cost / quantity * gone;
                quantity -= gone;
            }
        }

        return (quantity, cost);
    }

    /// <summary>
    /// EUR value of <paramref name="amount"/> of <paramref name="asset"/> on <paramref name="day"/>: 1:1 for EUR, the
    /// day's EURUSDT close for dollar stablecoins, else the asset's EUR close that day (or within the week before).
    /// </summary>
    public static decimal? EurValueOn(string asset, decimal amount, DateOnly day,
        IReadOnlyDictionary<DateOnly, decimal> usdPerEurCloses, decimal latestUsdPerEur,
        IReadOnlyDictionary<DateOnly, decimal> assetEurCloses)
    {
        if (asset == "EUR")
        {
            return amount;
        }

        if (UsdStable.Contains(asset) || asset == "USD")
        {
            var usdPerEur = UsdPerEurOn(day, usdPerEurCloses, latestUsdPerEur);
            return usdPerEur > 0 ? amount / usdPerEur : null;
        }

        return CloseOn(day, assetEurCloses) is { } close ? amount * close : null;
    }

    public static bool IsDollar(string asset) => UsdStable.Contains(asset) || asset == "USD";

    public static DateOnly DayOf(long unixMs) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime);

    /// <summary>EUR value of a reward: quantity × that day's close in EUR (else the current price).</summary>
    public static decimal RewardValue(decimal quantity, DateOnly day, IReadOnlyDictionary<DateOnly, decimal> eurCloses,
        decimal currentEurPrice)
    {
        var close = CloseOn(day, eurCloses) ?? currentEurPrice;
        return decimal.Round(quantity * close, 4);
    }

    private static decimal? CloseOn(DateOnly day, IReadOnlyDictionary<DateOnly, decimal> closes) =>
        closes.TryGetValue(day, out var c) && c > 0
            ? c
            : closes.Where(x => x.Key <= day && x.Key >= day.AddDays(-7) && x.Value > 0)
                .OrderByDescending(x => x.Key).Select(x => (decimal?)x.Value).FirstOrDefault();

    private static decimal UsdPerEurOn(DateOnly day, IReadOnlyDictionary<DateOnly, decimal> closes, decimal latest) =>
        CloseOn(day, closes) ?? latest;

    private static decimal Round(decimal value) => decimal.Round(value, 8);
}

/// <summary>Coins of one asset coming in (positive quantity, worth <c>EurValue</c>) or going out (negative).</summary>
internal readonly record struct CostEvent(long Time, decimal Quantity, decimal EurValue, string Kind = "")
{
    public static CostEvent In(long time, decimal quantity, decimal eurValue, string kind = "") =>
        new(time, quantity, eurValue, kind);

    public static CostEvent Out(long time, decimal quantity, string kind = "") => new(time, -quantity, 0, kind);
}
