namespace Finance.Domain.Loans;

/// <summary>A rate in force from a date (annual nominal rate, TAN, in percent).</summary>
public sealed record RatePeriod(DateOnly EffectiveFrom, decimal AnnualRatePercent);

/// <summary>An early repayment: shorten the term (same instalment) or lower the instalment (same term).</summary>
public sealed record Prepayment(DateOnly On, decimal Amount, PrepaymentMode Mode);

/// <summary>One instalment of the plan.</summary>
public sealed record Instalment(int Number, DateOnly Date, decimal AnnualRatePercent, decimal Payment, decimal Interest,
    decimal Principal, decimal Prepaid, decimal Balance);

/// <summary>Where a loan stands on a day, from its plan.</summary>
public sealed record LoanStatus(
    decimal Outstanding,
    int InstalmentsPaid,
    int InstalmentsLeft,
    Instalment? Next,
    DateOnly? EndDate,
    decimal InterestPaid,
    decimal InterestLeft,
    decimal CurrentRatePercent);

/// <summary>
/// The French amortisation plan used by Portuguese banks: a constant instalment of interest + capital, recalculated
/// over the remaining term whenever the rate changes (variable-rate revisions) or an early repayment asks to lower
/// it; an early repayment that shortens the term keeps the instalment and ends the loan sooner. Amounts are rounded
/// to the cent each month, as on a bank statement; the last instalment settles whatever is left.
/// </summary>
public static class LoanSchedule
{
    public const int MaxMonths = 12 * 60;

    public static IReadOnlyList<Instalment> Build(decimal principal, DateOnly firstPaymentOn, int termMonths,
        IReadOnlyList<RatePeriod> rates, IReadOnlyList<Prepayment> prepayments)
    {
        var plan = new List<Instalment>();
        if (principal <= 0 || termMonths <= 0 || rates.Count == 0)
        {
            return plan;
        }

        var ordered = rates.OrderBy(r => r.EffectiveFrom).ToList();
        var pending = new Queue<Prepayment>(prepayments.Where(p => p.Amount > 0).OrderBy(p => p.On));
        var balance = principal;
        var remaining = termMonths;
        decimal? payment = null;
        decimal? rateInForce = null;

        for (var n = 1; balance > 0 && n <= MaxMonths; n++)
        {
            var date = firstPaymentOn.AddMonths(n - 1);
            var annual = RateOn(ordered, date);
            var r = annual / 1200m;

            // Early repayments made since the previous instalment reduce the balance before this one.
            decimal prepaid = 0;
            var lower = false;
            var shorten = false;
            while (pending.TryPeek(out var p) && p.On <= date)
            {
                pending.Dequeue();
                var amount = Math.Min(p.Amount, balance);
                balance -= amount;
                prepaid += amount;
                lower |= p.Mode == PrepaymentMode.ReducePayment;
                shorten |= p.Mode == PrepaymentMode.ReduceTerm;
            }

            if (balance <= 0)
            {
                plan.Add(new Instalment(n, date, annual, 0, 0, 0, prepaid, 0));
                break;
            }

            if (shorten && payment is { } kept)
            {
                remaining = MonthsToRepay(balance, r, kept);
            }

            if (payment is null || annual != rateInForce || lower)
            {
                payment = Annuity(balance, r, Math.Max(remaining, 1));
            }

            rateInForce = annual;
            var interest = Round(balance * r);
            var capital = payment.Value - interest;
            if (capital >= balance || remaining <= 1)
            {
                capital = balance;
            }

            balance -= capital;
            plan.Add(new Instalment(n, date, annual, interest + capital, interest, capital, prepaid, balance));
            remaining--;
        }

        return plan;
    }

    /// <summary>The plan read on <paramref name="asOf"/>: instalments dated up to that day are paid.</summary>
    public static LoanStatus StatusOn(IReadOnlyList<Instalment> plan, decimal principal, DateOnly asOf)
    {
        var paid = plan.Where(i => i.Date <= asOf).ToList();
        var left = plan.Where(i => i.Date > asOf && i.Payment > 0).ToList();
        var outstanding = paid.Count > 0 ? paid[^1].Balance : principal;
        var next = left.FirstOrDefault();
        return new LoanStatus(
            outstanding,
            paid.Count(i => i.Payment > 0),
            left.Count,
            next,
            plan.LastOrDefault(i => i.Payment > 0)?.Date,
            paid.Sum(i => i.Interest),
            left.Sum(i => i.Interest),
            next?.AnnualRatePercent ?? paid.LastOrDefault()?.AnnualRatePercent ?? 0);
    }

    public static decimal RateOn(IReadOnlyList<RatePeriod> ordered, DateOnly date) =>
        ordered.LastOrDefault(r => r.EffectiveFrom <= date)?.AnnualRatePercent ?? ordered[0].AnnualRatePercent;

    /// <summary>Constant instalment that repays <paramref name="balance"/> in <paramref name="months"/>.</summary>
    public static decimal Annuity(decimal balance, decimal monthlyRate, int months)
    {
        if (monthlyRate == 0)
        {
            return Round(balance / months);
        }

        var factor = (decimal)Math.Pow(1 + (double)monthlyRate, -months);
        return Round(balance * monthlyRate / (1 - factor));
    }

    /// <summary>Months needed to repay <paramref name="balance"/> paying <paramref name="payment"/> a month.</summary>
    public static int MonthsToRepay(decimal balance, decimal monthlyRate, decimal payment)
    {
        if (monthlyRate == 0)
        {
            return (int)Math.Ceiling(balance / payment);
        }

        var x = 1 - (double)(balance * monthlyRate / payment);
        return x <= 0 ? MaxMonths : (int)Math.Ceiling(-Math.Log(x) / Math.Log(1 + (double)monthlyRate));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
