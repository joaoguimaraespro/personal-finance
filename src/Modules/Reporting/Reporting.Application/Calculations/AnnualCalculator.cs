using SharedKernel;

namespace Reporting.Application.Calculations;

public sealed record AnnualRow(
    MonthlySummary Month,
    decimal CumulativeInvested,
    decimal CumulativeSaved,
    bool HasIncome);

public sealed record AnnualTotals(
    decimal Income,
    decimal ExpenseBudget,
    decimal FixedExpenses,
    decimal VariableExpenses,
    decimal TotalExpenses,
    decimal Invested,
    decimal Saved,
    decimal NetBalance,
    decimal FreeCashFlow,
    decimal? AverageMonthlySavingsRate,
    decimal? WeightedSavingsRate,
    decimal? WeightedInvestmentRate,
    decimal? WeightedSavingsOnlyRate);

public sealed record AnnualSummary(int Year, IReadOnlyList<AnnualRow> Months, AnnualTotals Totals);

/// <summary>Port of the "📅 Resumo Anual" sheet (rows 4–16, columns B–O).</summary>
public static class AnnualCalculator
{
    public static AnnualSummary Compute(int year, IReadOnlyList<MonthlySummary> months)
    {
        var ordered = months.OrderBy(m => m.Period).ToList();
        if (ordered.Count != 12 || ordered.Any(m => m.Period.Year != year))
        {
            throw new ArgumentException("Expected exactly the 12 months of the year.", nameof(months));
        }

        decimal cumInvested = 0, cumSaved = 0;
        var rows = new List<AnnualRow>(12);
        foreach (var m in ordered)
        {
            cumInvested += m.Invested; // L = IFERROR(prev,0) + D11 + D12
            cumSaved += m.Saved;       // M = IFERROR(prev,0) + D13 + D14
            rows.Add(new AnnualRow(m, cumInvested, cumSaved, m.Income != 0));
        }

        var income = ordered.Sum(m => m.Income);
        var invested = ordered.Sum(m => m.Invested);
        var saved = ordered.Sum(m => m.Saved);
        var rates = ordered.Where(m => m.SavingsRate is not null).Select(m => m.SavingsRate!.Value).ToList();

        var totals = new AnnualTotals(
            income,
            ordered.Sum(m => m.ExpenseBudget ?? 0),
            ordered.Sum(m => m.FixedExpenses),
            ordered.Sum(m => m.VariableExpenses),
            ordered.Sum(m => m.TotalExpenses),
            invested,
            saved,
            ordered.Sum(m => m.NetBalance),
            ordered.Sum(m => m.FreeCashFlow),
            // J16 = AVERAGEIF(J4:J15,"<>") — unweighted mean of the monthly rates, kept for parity.
            AverageMonthlySavingsRate: rates.Count == 0 ? null : decimal.Round(rates.Average(), 6),
            // Income-weighted rates: what share of the year's income was actually set aside.
            WeightedSavingsRate: MonthlyCalculator.Ratio(invested + saved, income),
            WeightedInvestmentRate: MonthlyCalculator.Ratio(invested, income),
            WeightedSavingsOnlyRate: MonthlyCalculator.Ratio(saved, income));

        return new AnnualSummary(year, rows, totals);
    }
}
