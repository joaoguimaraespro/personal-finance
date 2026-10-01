using Investments.Application.Abstractions;
using Investments.Application.Sync;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Investments.Application.Portfolio;

/// <summary>
/// Writes today's valuation per broker account from positions and cash, so performance history accrues even
/// for brokers whose APIs expose only current state. Broker-reported history (IBKR NAV) is never overwritten;
/// a reconstructed estimate for today is replaced by the real valuation.
/// </summary>
public sealed class PortfolioSnapshotter(IInvestmentsDb db, PortfolioQueries portfolio, TimeProvider clock)
{
    public async Task<int> SnapshotTodayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var summary = await portfolio.SummaryAsync(new PortfolioScope(), ct);
        var written = 0;
        foreach (var account in summary.Accounts)
        {
            var flows = await db.CashMovements.AsNoTracking()
                .Where(c => c.AccountId == account.AccountId &&
                            (c.Type == CashMovementType.Deposit || c.Type == CashMovementType.Withdrawal))
                .Select(c => new { c.OccurredAtUtc, c.BaseAmount })
                .ToListAsync(ct);
            var todayFlow = flows.Where(f => DateOnly.FromDateTime(f.OccurredAtUtc.UtcDateTime) == today)
                .Sum(f => f.BaseAmount);

            var existing = await db.PortfolioSnapshots
                .FirstOrDefaultAsync(s => s.AccountId == account.AccountId && s.Date == today, ct);
            if (existing is null)
            {
                db.PortfolioSnapshots.Add(PortfolioSnapshot.Create(account.AccountId, today, account.MarketValue,
                    account.Cash, todayFlow, SnapshotOrigins.Computed));
                written++;
            }
            else if (existing.Origin is SnapshotOrigins.Computed or SnapshotOrigins.Reconstructed)
            {
                existing.Update(account.MarketValue, account.Cash, todayFlow, SnapshotOrigins.Computed);
                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }
}
