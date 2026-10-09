using SharedKernel;

namespace Reporting.Application.Calculations;

/// <summary>
/// Raw sums for one month, in EUR. Everything the spreadsheet computed derives from these. <see cref="ByBucket"/> is
/// net of investment sales; <see cref="InvestmentPurchases"/> and <see cref="InvestmentSales"/> are the gross sides.
/// </summary>
public sealed record MonthTotals(
    YearMonth Period,
    decimal Income,
    decimal FixedExpenses,
    decimal VariableExpenses,
    IReadOnlyDictionary<Guid, decimal> ByBucket,
    IReadOnlyDictionary<Guid, decimal> ByCategory,
    IReadOnlyDictionary<Guid, decimal> IncomeByCategory,
    int TransactionCount,
    decimal InvestmentPurchases = 0,
    decimal InvestmentSales = 0,
    IReadOnlyDictionary<Guid, decimal>? FromBrokers = null)
{
    public static MonthTotals Empty(YearMonth period) =>
        new(period, 0, 0, 0, new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>(), 0);
}

/// <summary>Bucket and group membership needed to split contributions into "invested" and "saved".</summary>
public sealed record BucketInfo(Guid Id, string Name, bool IsInvestment);
