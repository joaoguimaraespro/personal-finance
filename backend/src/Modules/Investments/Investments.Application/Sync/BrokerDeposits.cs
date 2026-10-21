using Finance.Application.Abstractions;
using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Sync;

/// <summary>
/// Synced deposits and withdrawals per broker account (EUR at each movement's own rate): the ones counted as they
/// are, per month, and the ones the owner reviews.
/// </summary>
public sealed class BrokerDeposits(IInvestmentsDb db) : IBrokerDeposits
{
    public async Task<IReadOnlyList<BrokerDepositMonth>> MonthlyAsync(YearMonth from, YearMonth to,
        CancellationToken ct)
    {
        var start = new DateTimeOffset(from.FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.LastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await Synced()
            .Where(c => c.Review == CashMovementReview.Counted && c.OccurredAtUtc >= start && c.OccurredAtUtc < end)
            .GroupBy(c => new { c.AccountId, c.OccurredAtUtc.Year, c.OccurredAtUtc.Month })
            .Select(g => new { g.Key.AccountId, g.Key.Year, g.Key.Month, Net = g.Sum(c => c.BaseAmount) })
            .ToListAsync(ct);
        return rows.Select(r => new BrokerDepositMonth(r.AccountId, new YearMonth(r.Year, r.Month), r.Net)).ToList();
    }

    public async Task<IReadOnlyList<BrokerMovement>> ReviewableAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await Synced()
            .Where(c => c.Review != CashMovementReview.Counted && c.OccurredAtUtc >= start && c.OccurredAtUtc < end)
            .OrderBy(c => c.OccurredAtUtc)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<BrokerMovement?> FindAsync(Guid id, CancellationToken ct) =>
        await Synced().FirstOrDefaultAsync(c => c.Id == id && c.Review != CashMovementReview.Counted, ct) is { } c
            ? ToDto(c)
            : null;

    public async Task<bool> ResolveAsync(Guid id, Guid? transactionId, CancellationToken ct)
    {
        var movement = await db.CashMovements.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (movement is null || movement.Review == CashMovementReview.Counted)
        {
            return false;
        }

        if (transactionId is { } tx)
        {
            movement.MarkRecorded(tx);
        }
        else
        {
            movement.Ignore();
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<CashMovement> Synced() => db.CashMovements.AsNoTracking()
        .Where(c => c.Source != DataSource.Manual &&
                    (c.Type == CashMovementType.Deposit || c.Type == CashMovementType.Withdrawal));

    private static BrokerMovement ToDto(CashMovement c) => new(c.Id, c.AccountId, c.Amount, c.Currency,
        c.BaseAmount, DateOnly.FromDateTime(c.OccurredAtUtc.UtcDateTime), c.Description, c.Review switch
        {
            CashMovementReview.Recorded => BrokerMovementState.Recorded,
            CashMovementReview.Ignored => BrokerMovementState.Ignored,
            _ => BrokerMovementState.Pending,
        }, c.RecordedTransactionId);
}
