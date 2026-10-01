using Finance.Domain.Accounts;
using SharedKernel;

namespace Finance.Domain.Interest;

/// <summary>A rate period as the calculator sees it. Percentages, not fractions.</summary>
public sealed record RatePeriod(DateOnly EffectiveFrom, decimal AnnualRatePercent, decimal WithholdingPercent);

/// <param name="OpeningBalance">The account's opening balance, in the account's currency.</param>
/// <param name="OpeningOn">Accrual never starts before this date (the account did not exist).</param>
/// <param name="Flows">Net signed movement per day (inflows positive), excluding estimated interest.
/// Real interest the bank paid is included like any other inflow.</param>
/// <param name="Rates">Rate periods; the first one's effective date is when accrual starts.</param>
/// <param name="Through">Last day to accrue (inclusive): today, or the day before the account was archived.</param>
/// <param name="SettledMonths">Months already settled with real data (reconciled or real interest in the ledger):
/// they get no estimate and no estimated credit.</param>
public sealed record AccrualInput(
    decimal OpeningBalance,
    DateOnly OpeningOn,
    IReadOnlyDictionary<DateOnly, decimal> Flows,
    IReadOnlyList<RatePeriod> Rates,
    InterestPayout Payout,
    DateOnly Through,
    IReadOnlySet<YearMonth> SettledMonths);

/// <summary>Estimated interest of one month. <see cref="Net"/> is rounded to cents; it is what lands in the ledger.</summary>
public sealed record MonthAccrual(YearMonth Month, decimal Gross, decimal Net, int DaysAccrued, DateOnly LastDay);

/// <summary>
/// Daily accrual on a calendar of 365 days (weekends and holidays included):
/// <c>interest(d) = max(balance(d), 0) × TANB / 365</c>, net of withholding.
/// <list type="bullet">
/// <item><c>balance(d)</c> is the end-of-day balance: opening balance plus every flow dated on or before <c>d</c>,
/// plus estimated interest already credited.</item>
/// <item>The divisor is 365 in leap years too, so 29 February earns one extra day (what Portuguese banks quote).</item>
/// <item>Monthly payout: the month's rounded net estimate is credited on its last day, so it earns interest from the
/// next month on. Daily payout: each day's net interest is credited that day (daily compounding).</item>
/// <item>A zero or negative balance earns nothing; a 0 % period stops accrual.</item>
/// </list>
/// </summary>
public static class InterestAccrual
{
    public const int DayCountBasis = 365;

    public static IReadOnlyList<MonthAccrual> Compute(AccrualInput input)
    {
        if (input.Rates.Count == 0)
        {
            return [];
        }

        var rates = input.Rates.OrderBy(r => r.EffectiveFrom).ToList();
        var start = Max(input.OpeningOn, rates[0].EffectiveFrom);
        if (start > input.Through)
        {
            return [];
        }

        var balance = input.OpeningBalance + input.Flows.Where(f => f.Key < start).Sum(f => f.Value);
        var results = new List<MonthAccrual>();
        var rateIndex = 0;
        YearMonth? month = null;
        decimal monthGross = 0, monthNet = 0;
        var days = 0;

        for (var day = start; day <= input.Through; day = day.AddDays(1))
        {
            var current = YearMonth.From(day);
            if (month != current)
            {
                month = current;
                monthGross = monthNet = 0;
                days = 0;
            }

            while (rateIndex + 1 < rates.Count && rates[rateIndex + 1].EffectiveFrom <= day)
            {
                rateIndex++;
            }

            if (input.Flows.TryGetValue(day, out var flow))
            {
                balance += flow;
            }

            var settled = input.SettledMonths.Contains(current);
            var rate = rates[rateIndex];
            if (!settled && balance > 0 && rate.AnnualRatePercent > 0)
            {
                var gross = balance * rate.AnnualRatePercent / 100m / DayCountBasis;
                var net = gross * (1m - rate.WithholdingPercent / 100m);
                monthGross += gross;
                monthNet += net;
                days++;
                if (input.Payout == InterestPayout.Daily)
                {
                    balance += net;
                }
            }

            var monthEnds = day == current.LastDay || day == input.Through;
            if (monthEnds && !settled)
            {
                var rounded = Currency.Round(monthNet);
                results.Add(new MonthAccrual(current, Currency.Round(monthGross), rounded, days, day));
                if (input.Payout == InterestPayout.Monthly && day == current.LastDay)
                {
                    balance += rounded;
                }
            }
        }

        return results;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
