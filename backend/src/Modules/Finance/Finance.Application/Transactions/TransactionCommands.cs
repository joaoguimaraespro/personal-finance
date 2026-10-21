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
        TransactionDraft draft, DataSource source, CancellationToken ct, IReadOnlyList<ShareRequest>? shares = null,
        bool cardPayment = false)
    {
        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct, cardPayment);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        if (shares is { Count: > 0 } && await TransactionShares.ValidateAsync(db, resolved.Value, shares, ct) is { } invalid)
        {
            return invalid;
        }

        var created = Transaction.Create(resolved.Value, source);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Transactions.Add(created.Value);
        if (shares is { Count: > 0 } &&
            await TransactionShares.SyncAsync(db, created.Value, shares, DateTimeOffset.UtcNow, ct) is { } failed)
        {
            return failed;
        }

        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([created.Value.AccountId, created.Value.CounterAccountId], ct);
        return created.Value;
    }

    /// <param name="shares">The people's shares (ADR-0011): null keeps the current ones, empty removes them.</param>
    public static async Task<Result<Transaction>> UpdateAsync(IFinanceDb db, InterestAccrualService interest, Guid id,
        TransactionDraft draft, CancellationToken ct, IReadOnlyList<ShareRequest>? shares = null)
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

        if (transaction.IsShare)
        {
            return ShareErrors.EditTheExpense;
        }

        Guid?[] before = [transaction.AccountId, transaction.CounterAccountId];
        // A card payment already on its broker account stays editable there (category, description, shares…).
        var cardPayment = transaction.Type == TransactionType.Expense && draft.AccountId == transaction.AccountId;
        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct, cardPayment);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        if (shares is { Count: > 0 } && await TransactionShares.ValidateAsync(db, resolved.Value, shares, ct) is { } invalid)
        {
            return invalid;
        }

        var updated = transaction.Update(resolved.Value);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (await TransactionShares.SyncAsync(db, transaction, shares, DateTimeOffset.UtcNow, ct) is { } failed)
        {
            return failed;
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
        // Its people's shares go with it; deleting one share only means the owner takes that part on.
        foreach (var share in await db.Transactions.Where(t => t.SharedFromId == transaction.Id).ToListAsync(ct))
        {
            share.SoftDelete(now);
        }

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

        if (transaction.SharedFromId is { } expenseId && !await db.Transactions.AnyAsync(t => t.Id == expenseId, ct))
        {
            return TransactionErrors.NotFound;
        }

        // The shares deleted together with it come back with it.
        var deletedAt = transaction.DeletedAtUtc;
        foreach (var share in await db.Transactions.IgnoreQueryFilters()
                     .Where(t => t.SharedFromId == transaction.Id && t.DeletedAtUtc == deletedAt).ToListAsync(ct))
        {
            share.Restore();
        }

        transaction.Restore();
        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([transaction.AccountId, transaction.CounterAccountId], ct);
        return transaction;
    }
}
