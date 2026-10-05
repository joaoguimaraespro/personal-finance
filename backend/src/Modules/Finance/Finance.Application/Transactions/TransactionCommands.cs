using Finance.Application.Abstractions;
using Finance.Application.Interest;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Transactions;

/// <summary>
/// The ledger's write operations, shared by the HTTP endpoints and the AI gateway's write tools so both apply the
/// same reference checks, domain rules, audit trail (via the save interceptor) and interest recalculation.
/// </summary>
public static class TransactionCommands
{
    public static async Task<Result<Transaction>> CreateAsync(IFinanceDb db, InterestAccrualService interest,
        TransactionDraft draft, DataSource source, CancellationToken ct)
    {
        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var created = Transaction.Create(resolved.Value, source);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Transactions.Add(created.Value);
        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([created.Value.AccountId, created.Value.CounterAccountId], ct);
        return created.Value;
    }

    public static async Task<Result<Transaction>> UpdateAsync(IFinanceDb db, InterestAccrualService interest, Guid id,
        TransactionDraft draft, CancellationToken ct)
    {
        var transaction = await db.Transactions.FindAsync([id], ct);
        if (transaction is null)
        {
            return TransactionErrors.NotFound;
        }

        if (TransactionEndpoints.ReadOnlyError(transaction.Source) is { } readOnly)
        {
            return readOnly;
        }

        Guid?[] before = [transaction.AccountId, transaction.CounterAccountId];
        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var updated = transaction.Update(resolved.Value);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([.. before, transaction.AccountId, transaction.CounterAccountId], ct);
        return transaction;
    }

    /// <summary>Soft delete: the row stays (hidden) and can be restored; the audit trail records who did it.</summary>
    public static async Task<Result<Transaction>> DeleteAsync(IFinanceDb db, InterestAccrualService interest, Guid id,
        DateTimeOffset now, CancellationToken ct)
    {
        var transaction = await db.Transactions.FindAsync([id], ct);
        if (transaction is null)
        {
            return TransactionErrors.NotFound;
        }

        if (TransactionEndpoints.ReadOnlyError(transaction.Source) is { } readOnly)
        {
            return readOnly;
        }

        transaction.SoftDelete(now);
        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([transaction.AccountId, transaction.CounterAccountId], ct);
        return transaction;
    }

    public static async Task<Result<Transaction>> RestoreAsync(IFinanceDb db, InterestAccrualService interest, Guid id,
        CancellationToken ct)
    {
        var transaction = await db.Transactions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAtUtc != null, ct);
        if (transaction is null)
        {
            return TransactionErrors.NotFound;
        }

        transaction.Restore();
        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([transaction.AccountId, transaction.CounterAccountId], ct);
        return transaction;
    }
}
