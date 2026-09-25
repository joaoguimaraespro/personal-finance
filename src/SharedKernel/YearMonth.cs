using System.Globalization;

namespace SharedKernel;

/// <summary>A calendar month. Monthly figures are always derived from transactions within it, never stored.</summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public YearMonth Next() => From(FirstDay.AddMonths(1));

    public YearMonth Previous() => From(FirstDay.AddMonths(-1));

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (value is null || !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return false;
        }

        result = From(date);
        return true;
    }

    public int CompareTo(YearMonth other) => FirstDay.CompareTo(other.FirstDay);

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;
    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;
    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
