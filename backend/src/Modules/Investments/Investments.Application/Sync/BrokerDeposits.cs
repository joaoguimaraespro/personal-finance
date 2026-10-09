using Finance.Application.Abstractions;
using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Sync;

/// <summary>Synced deposits minus withdrawals per broker account and month (EUR at each movement's own rate).</summary>
public sealed class BrokerDeposits(IInvestmentsDb db) : IBrokerDeposits
{
    public async Task<IReadOnlyList<BrokerDepositMonth>> MonthlyAsync(YearMonth from, YearMonth to,
        CancellationToken ct)
    {
        var start = new DateTimeOffset(from.FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.LastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await db.CashMovements.AsNoTracking()
            .Where(c => c.Source != DataSource.Manual && c.OccurredAtUtc >= start && c.OccurredAtUtc < end &&
                        (c.Type == CashMovementType.Deposit || c.Type == CashMovementType.Withdrawal))
            .GroupBy(c => new { c.AccountId, c.OccurredAtUtc.Year, c.OccurredAtUtc.Month })
            .Select(g => new { g.Key.AccountId, g.Key.Year, g.Key.Month, Net = g.Sum(c => c.BaseAmount) })
            .ToListAsync(ct);
        return rows.Select(r => new BrokerDepositMonth(r.AccountId, new YearMonth(r.Year, r.Month), r.Net)).ToList();
    }
}
