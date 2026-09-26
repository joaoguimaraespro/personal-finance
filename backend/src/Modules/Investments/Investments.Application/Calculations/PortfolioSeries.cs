namespace Investments.Application.Calculations;

public readonly record struct AccountValuation(Guid AccountId, DateOnly Date, decimal Value);

public readonly record struct AccountFlow(Guid AccountId, DateOnly Date, decimal Amount);

public sealed record SeriesPoint(DateOnly Date, decimal Value, decimal NetFlow, decimal EndOfDayFlow,
    decimal CumulativeContributions)
{
    public decimal TotalFlow => NetFlow + EndOfDayFlow;

    public ValuationPoint ToValuation() => new(Date, Value, NetFlow, EndOfDayFlow);
}

public sealed record AggregatedSeries(IReadOnlyList<SeriesPoint> Points, IReadOnlyList<CashFlow> InvestorFlows);

/// <summary>
/// Combines per-account valuations into one portfolio series that performance maths can trust:
/// <list type="bullet">
/// <item>external flows are taken from the cash-movement ledger and attributed to the interval
///   (previous valuation, this valuation] — a deposit on a weekend or holiday is never mistaken for a gain;</item>
/// <item>accounts without a valuation on a date carry their last value forward;</item>
/// <item>an account that starts reporting later (a newly connected broker) enters as an inflow, not a return.</item>
/// </list>
/// </summary>
public static class PortfolioSeries
{
    public static AggregatedSeries Build(IReadOnlyList<AccountValuation> valuations, IReadOnlyList<AccountFlow> flows,
        DateOnly? from, DateOnly to)
    {
        var byAccount = valuations.Where(v => v.Date <= to)
            .GroupBy(v => v.AccountId)
            .ToDictionary(g => g.Key, g => g.GroupBy(v => v.Date).ToDictionary(d => d.Key, d => d.Last().Value));
        var firstDate = byAccount.ToDictionary(a => a.Key, a => a.Value.Keys.Min());
        var dates = byAccount.Values.SelectMany(v => v.Keys)
            .Where(d => from is null || d >= from)
            .Distinct().Order().ToList();
        var points = new List<SeriesPoint>();
        var investorFlows = new List<CashFlow>();
        if (dates.Count == 0)
        {
            return new AggregatedSeries(points, investorFlows);
        }

        var last = new Dictionary<Guid, decimal>();
        // Seed carried-forward values for accounts that started before the window.
        foreach (var (account, series) in byAccount)
        {
            var before = series.Keys.Where(d => d < dates[0]).DefaultIfEmpty().Max();
            if (before != default)
            {
                last[account] = series[before];
            }
        }

        decimal ContributionsUpTo(DateOnly d) => flows.Where(f => f.Date <= d).Sum(f => f.Amount);

        DateOnly? previous = null;
        foreach (var date in dates)
        {
            var netFlow = 0m;
            var entering = 0m;
            foreach (var (account, series) in byAccount)
            {
                var started = firstDate[account];
                if (started > date)
                {
                    continue;
                }

                if (series.TryGetValue(date, out var value))
                {
                    last[account] = value;
                }

                if (previous is null)
                {
                    continue; // The first point is the base; flows before it are part of its value.
                }

                if (started == date)
                {
                    entering += last[account]; // Newly reporting account: its value arrives as an end-of-day inflow.
                }
                else
                {
                    netFlow += flows.Where(f => f.AccountId == account && f.Date > previous && f.Date <= date)
                        .Sum(f => f.Amount);
                }
            }

            var total = last.Where(kv => firstDate[kv.Key] <= date).Sum(kv => kv.Value);
            points.Add(new SeriesPoint(date, total, netFlow, entering, ContributionsUpTo(date)));
            if (previous is null)
            {
                investorFlows.Add(new CashFlow(date, -total));
            }
            else if (netFlow + entering != 0)
            {
                investorFlows.Add(new CashFlow(date, -(netFlow + entering)));
            }

            previous = date;
        }

        investorFlows.Add(new CashFlow(points[^1].Date, points[^1].Value));
        return new AggregatedSeries(points, investorFlows);
    }
}
