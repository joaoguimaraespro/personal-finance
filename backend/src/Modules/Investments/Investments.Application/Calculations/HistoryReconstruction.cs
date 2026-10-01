namespace Investments.Application.Calculations;

/// <summary>An executed fill: signed quantity (+ bought, − sold), price in <see cref="Currency"/>, EUR cash impact.</summary>
public readonly record struct LedgerFill(DateOnly Date, Guid SecurityId, decimal SignedQuantity, decimal Price,
    string Currency, decimal CashBase);

/// <summary>Any other cash change in EUR (deposits, withdrawals, fees, interest, dividends).</summary>
public readonly record struct LedgerCash(DateOnly Date, decimal AmountBase, bool IsExternalFlow);

public readonly record struct PriceClose(DateOnly Date, decimal Close, string Currency);

/// <summary>EUR value of one unit of a currency on a date, or null when unknown.</summary>
public delegate decimal? EurPerUnit(string currency, DateOnly date);

public sealed record ReconstructionInput(
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<Guid, decimal> QuantitiesNow,
    DateOnly CashAnchorDate,
    decimal CashAnchorBase,
    IReadOnlyList<LedgerFill> Fills,
    IReadOnlyList<LedgerCash> Cash,
    IReadOnlyDictionary<Guid, IReadOnlyList<PriceClose>> Closes,
    EurPerUnit Fx);

/// <param name="EstimatedHoldings">Holdings valued from a trade price (no public close within a week) or without
/// any price at all.</param>
public sealed record ReconstructedDay(DateOnly Date, decimal MarketValue, decimal Cash, decimal NetFlow,
    int EstimatedHoldings);

/// <summary>
/// Rebuilds daily valuations of a broker account from its ledger, for brokers that report no history.
/// <list type="bullet">
/// <item>Holdings are rolled <b>backwards</b> from the current positions (qty on d = qty now − fills after d), and
///   cash backwards from the first real snapshot. The series therefore ends exactly where the real data starts,
///   even when the ledger misses old records.</item>
/// <item>Each holding is valued at the latest public close on or before the day (weekends and holidays carry the
///   last close), converted to EUR at that day's reference rate.</item>
/// <item>Without a recent close, the latest trade price stands in and the day is flagged as estimated.</item>
/// </list>
/// </summary>
public static class HistoryReconstruction
{
    /// <summary>A close older than this no longer counts as a market price for the day.</summary>
    public const int StaleCloseDays = 7;

    public static IReadOnlyList<ReconstructedDay> Build(ReconstructionInput input)
    {
        var days = new List<ReconstructedDay>();
        if (input.From > input.To)
        {
            return days;
        }

        var fillsByDate = input.Fills.GroupBy(f => f.Date).ToDictionary(g => g.Key, g => g.ToList());
        var cashByDate = input.Cash.GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.ToList());

        // Holdings and cash at the end of input.To, rolled back from the anchors.
        var quantities = new Dictionary<Guid, decimal>(input.QuantitiesNow);
        foreach (var fill in input.Fills.Where(f => f.Date > input.To))
        {
            quantities[fill.SecurityId] = quantities.GetValueOrDefault(fill.SecurityId) - fill.SignedQuantity;
        }

        var cash = input.CashAnchorBase;
        foreach (var fill in input.Fills.Where(f => f.Date > input.To && f.Date <= input.CashAnchorDate))
        {
            cash -= fill.CashBase;
        }

        foreach (var c in input.Cash.Where(c => c.Date > input.To && c.Date <= input.CashAnchorDate))
        {
            cash -= c.AmountBase;
        }

        var closes = input.Closes.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(c => c.Date).ToArray());
        var tradePrices = input.Fills.Where(f => f.Price > 0)
            .GroupBy(f => f.SecurityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Date)
                .Select(f => new PriceClose(f.Date, f.Price, f.Currency)).ToArray());

        for (var date = input.To; date >= input.From; date = date.AddDays(-1))
        {
            var marketValue = 0m;
            var estimated = 0;
            foreach (var (securityId, quantity) in quantities)
            {
                if (quantity <= 0.0000001m)
                {
                    estimated += quantity < -0.0001m ? 1 : 0; // More sold than ever bought: the ledger is incomplete.
                    continue;
                }

                var (value, isEstimate) = Value(securityId, quantity, date, closes, tradePrices, input.Fx);
                marketValue += value;
                estimated += isEstimate ? 1 : 0;
            }

            var dayCash = cashByDate.GetValueOrDefault(date) ?? [];
            var netFlow = dayCash.Where(c => c.IsExternalFlow).Sum(c => c.AmountBase);
            days.Add(new ReconstructedDay(date, decimal.Round(marketValue, 4), decimal.Round(cash, 4),
                decimal.Round(netFlow, 4), estimated));

            // Step back to the end of the previous day: undo today's fills and cash.
            foreach (var fill in fillsByDate.GetValueOrDefault(date) ?? [])
            {
                quantities[fill.SecurityId] = quantities.GetValueOrDefault(fill.SecurityId) - fill.SignedQuantity;
                cash -= fill.CashBase;
            }

            cash -= dayCash.Sum(c => c.AmountBase);
        }

        days.Reverse();
        return days;
    }

    private static (decimal Value, bool Estimated) Value(Guid securityId, decimal quantity, DateOnly date,
        Dictionary<Guid, PriceClose[]> closes, Dictionary<Guid, PriceClose[]> tradePrices, EurPerUnit fx)
    {
        var close = LatestOnOrBefore(closes.GetValueOrDefault(securityId), date);
        var trade = LatestOnOrBefore(tradePrices.GetValueOrDefault(securityId), date);
        PriceClose? price;
        bool estimated;
        if (close is { } c && date.DayNumber - c.Date.DayNumber <= StaleCloseDays)
        {
            // A trade on a later day than the close (e.g. a fill on a market holiday of the listing) is fresher.
            price = trade is { } t && t.Date > c.Date ? t : c;
            estimated = false;
        }
        else
        {
            price = trade ?? close ?? FirstAfter(closes.GetValueOrDefault(securityId), date)
                ?? FirstAfter(tradePrices.GetValueOrDefault(securityId), date);
            estimated = true;
        }

        if (price is not { } p || fx(p.Currency, date) is not { } factor)
        {
            return (0, true);
        }

        return (quantity * p.Close * factor, estimated);
    }

    private static PriceClose? LatestOnOrBefore(PriceClose[]? sorted, DateOnly date)
    {
        if (sorted is null || sorted.Length == 0)
        {
            return null;
        }

        int lo = 0, hi = sorted.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid].Date <= date)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found < 0 ? null : sorted[found];
    }

    private static PriceClose? FirstAfter(PriceClose[]? sorted, DateOnly date) =>
        sorted?.FirstOrDefault(p => p.Date > date) is { Close: > 0 } p ? p : null;
}
