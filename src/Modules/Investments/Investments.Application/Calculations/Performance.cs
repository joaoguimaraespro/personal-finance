namespace Investments.Application.Calculations;

/// <summary>A daily valuation with the external cash flow (deposits − withdrawals) that happened that day.</summary>
public readonly record struct ValuationPoint(DateOnly Date, decimal Value, decimal NetFlow);

public readonly record struct CashFlow(DateOnly Date, decimal Amount);

public static class Performance
{
    /// <summary>
    /// Time-weighted return: chain-links daily returns so the timing and size of deposits don't distort the
    /// result. Flows are assumed to arrive at the start of the day they are booked:
    /// rᵢ = Vᵢ / (Vᵢ₋₁ + Fᵢ) − 1.
    /// </summary>
    public static decimal? TimeWeightedReturn(IReadOnlyList<ValuationPoint> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var ordered = points.OrderBy(p => p.Date).ToList();
        var growth = 1m;
        var linked = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var start = ordered[i - 1].Value + ordered[i].NetFlow;
            if (start <= 0)
            {
                continue; // Empty account: no exposure, no return for this period.
            }

            growth *= ordered[i].Value / start;
            linked++;
        }

        return linked == 0 ? null : decimal.Round(growth - 1m, 6);
    }

    /// <summary>
    /// Money-weighted return (XIRR, annualised) from the investor's cash flows: deposits negative, withdrawals
    /// and the final value positive. Solved with Newton–Raphson, falling back to bisection. Iteration runs in
    /// double precision — the result is an estimate by nature — and is returned rounded to 6 decimals.
    /// </summary>
    public static decimal? Xirr(IReadOnlyList<CashFlow> flows)
    {
        var nonZero = flows.Where(f => f.Amount != 0).OrderBy(f => f.Date).ToList();
        if (nonZero.Count < 2 || !nonZero.Any(f => f.Amount > 0) || !nonZero.Any(f => f.Amount < 0))
        {
            return null;
        }

        var t0 = nonZero[0].Date;
        var years = nonZero.Select(f => (f.Date.DayNumber - t0.DayNumber) / 365.0).ToArray();
        var amounts = nonZero.Select(f => (double)f.Amount).ToArray();
        if (years[^1] <= 0)
        {
            return null;
        }

        double Npv(double rate)
        {
            var sum = 0.0;
            for (var i = 0; i < amounts.Length; i++)
            {
                sum += amounts[i] / Math.Pow(1 + rate, years[i]);
            }

            return sum;
        }

        double Derivative(double rate)
        {
            var sum = 0.0;
            for (var i = 0; i < amounts.Length; i++)
            {
                sum -= years[i] * amounts[i] / Math.Pow(1 + rate, years[i] + 1);
            }

            return sum;
        }

        var r = 0.1;
        for (var i = 0; i < 100; i++)
        {
            var value = Npv(r);
            var slope = Derivative(r);
            if (Math.Abs(slope) < 1e-12)
            {
                break;
            }

            var next = r - value / slope;
            if (double.IsNaN(next) || next <= -0.999999)
            {
                break;
            }

            if (Math.Abs(next - r) < 1e-10)
            {
                return decimal.Round((decimal)next, 6);
            }

            r = next;
        }

        // Bisection on a bracket where the NPV changes sign.
        double lo = -0.999, hi = 10.0;
        double fLo = Npv(lo), fHi = Npv(hi);
        if (double.IsNaN(fLo) || double.IsNaN(fHi) || Math.Sign(fLo) == Math.Sign(fHi))
        {
            return null;
        }

        for (var i = 0; i < 300; i++)
        {
            var mid = (lo + hi) / 2;
            var fMid = Npv(mid);
            if (Math.Abs(fMid) < 1e-9 || hi - lo < 1e-12)
            {
                return decimal.Round((decimal)mid, 6);
            }

            if (Math.Sign(fMid) == Math.Sign(fLo))
            {
                lo = mid;
                fLo = fMid;
            }
            else
            {
                hi = mid;
            }
        }

        return decimal.Round((decimal)((lo + hi) / 2), 6);
    }
}
