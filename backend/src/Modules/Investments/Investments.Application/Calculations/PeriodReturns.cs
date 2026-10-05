namespace Investments.Application.Calculations;

/// <summary>The period a return is measured over, as the portfolio page offers it.</summary>
public enum ReturnPeriod
{
    /// <summary>Since the first deposit, trade or holding: value minus net contributions.</summary>
    All,

    /// <summary>The last month (same day of the previous month).</summary>
    OneMonth,

    /// <summary>Since 1 January (base: the last value of the previous year).</summary>
    YearToDate,

    /// <summary>The last twelve months.</summary>
    OneYear,
}

/// <summary>
/// Return over a period, measured from the value at its start.
/// </summary>
/// <param name="From">Date of the starting value: the last valuation on or before the period's base date, or the
/// first valuation when history starts later (<paramref name="Partial"/>).</param>
/// <param name="TimeWeightedReturn">Chain-linked daily returns: not distorted by deposits and withdrawals.</param>
/// <param name="Gain">EndValue − StartValue − NetFlows: what the market added in the period, in EUR.</param>
/// <param name="Partial">History begins after the period's start, so the figures cover less than the period.</param>
public sealed record PeriodResult(DateOnly From, DateOnly To, decimal StartValue, decimal EndValue, decimal NetFlows,
    decimal Gain, decimal? TimeWeightedReturn, bool Partial);

public static class PeriodReturns
{
    /// <summary>Parses the API value ("1M", "YTD", "1Y", "ALL"; case-insensitive); unknown or empty is All.</summary>
    public static ReturnPeriod Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "1M" => ReturnPeriod.OneMonth,
        "YTD" => ReturnPeriod.YearToDate,
        "1Y" => ReturnPeriod.OneYear,
        _ => ReturnPeriod.All,
    };

    public static string Code(ReturnPeriod period) => period switch
    {
        ReturnPeriod.OneMonth => "1M",
        ReturnPeriod.YearToDate => "YTD",
        ReturnPeriod.OneYear => "1Y",
        _ => "ALL",
    };

    /// <summary>
    /// The day whose closing value the period is measured from: a month or a year before today, or 31 December
    /// for the year to date. Null for All.
    /// </summary>
    public static DateOnly? BaseDate(ReturnPeriod period, DateOnly today) => period switch
    {
        ReturnPeriod.OneMonth => today.AddMonths(-1),
        ReturnPeriod.YearToDate => new DateOnly(today.Year, 1, 1).AddDays(-1),
        ReturnPeriod.OneYear => today.AddYears(-1),
        _ => null,
    };

    /// <summary>
    /// The first date of the series to measure from <paramref name="baseDate"/>: the last valuation on or before
    /// it (a weekend or holiday base uses the last value known then), else the first valuation (history starts
    /// within the period). Null without valuations.
    /// </summary>
    public static DateOnly? StartDate(IEnumerable<AccountValuation> valuations, DateOnly baseDate, DateOnly to)
    {
        var dates = valuations.Where(v => v.Date <= to).Select(v => v.Date).ToList();
        if (dates.Count == 0)
        {
            return null;
        }

        var before = dates.Where(d => d <= baseDate).ToList();
        return before.Count > 0 ? before.Max() : dates.Min();
    }

    /// <summary>
    /// TWR and gain over (start, to]. Valuations are daily account values (the last one may be today's live value);
    /// flows are deposits and withdrawals. Null when fewer than two valuations fall in the period.
    /// </summary>
    public static PeriodResult? Compute(IReadOnlyList<AccountValuation> valuations, IReadOnlyList<AccountFlow> flows,
        DateOnly baseDate, DateOnly to)
    {
        if (StartDate(valuations, baseDate, to) is not { } start)
        {
            return null;
        }

        var points = PortfolioSeries.Build(valuations, flows, start, to).Points;
        if (points.Count < 2)
        {
            return null;
        }

        var netFlows = points.Skip(1).Sum(p => p.TotalFlow);
        return new PeriodResult(points[0].Date, points[^1].Date, points[0].Value, points[^1].Value, netFlows,
            points[^1].Value - points[0].Value - netFlows,
            Performance.TimeWeightedReturn(points.Select(p => p.ToValuation()).ToList()),
            points[0].Date > baseDate);
    }
}
