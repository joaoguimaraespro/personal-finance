using Finance.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Buckets;

/// <summary>What deleting an allocation bucket touches.</summary>
public sealed record BucketDeletionPreview(Guid Id, string Name, bool IsSystem, int Transactions, int Recurring,
    int LinkedAccounts, int BudgetTargets);

/// <summary>
/// Deleting an allocation bucket for good. Its savings and investment entries, recurring items and the broker
/// accounts linked to it move to another bucket of the same group; its budget targets and monthly checks go. Built-in
/// buckets come back with the app, so they can only be archived.
/// </summary>
public static class BucketDeletion
{
    public static readonly Error BuiltIn = Error.Validation("Bucket.Delete",
        "Built-in buckets can be archived, not deleted.");

    public static readonly Error MoveRequired = Error.Validation("Bucket.MoveTo",
        "This bucket is in use: choose another bucket of the same group to move it to.");

    public static async Task<Result<BucketDeletionPreview>> PreviewAsync(IFinanceDb db, Guid id, CancellationToken ct)
    {
        var bucket = await db.Buckets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bucket is null)
        {
            return BucketEndpoints.NotFound;
        }

        return new BucketDeletionPreview(bucket.Id, bucket.Name, bucket.IsSystem,
            await db.Transactions.IgnoreQueryFilters().CountAsync(t => t.BucketId == id, ct),
            await db.RecurringTransactions.CountAsync(r => r.BucketId == id, ct),
            await db.Accounts.CountAsync(a => a.AllocationBucketId == id, ct),
            await db.Budgets.SelectMany(b => b.Items).CountAsync(i => i.BucketId == id, ct));
    }

    public static async Task<Result<BucketDeletionPreview>> DeleteAsync(IFinanceDb db, Guid id, Guid? moveTo,
        CancellationToken ct)
    {
        var preview = await PreviewAsync(db, id, ct);
        if (preview.IsFailure)
        {
            return preview;
        }

        var p = preview.Value;
        if (p.IsSystem)
        {
            return BuiltIn;
        }

        var bucket = await db.Buckets.FirstAsync(b => b.Id == id, ct);
        var target = moveTo is null
            ? null
            : await db.Buckets.FirstOrDefaultAsync(b => b.Id == moveTo && b.Id != id && b.ArchivedAtUtc == null &&
                                                       b.Group == bucket.Group, ct);
        if ((p.Transactions > 0 || p.Recurring > 0) && target is null)
        {
            return MoveRequired;
        }

        foreach (var t in await db.Transactions.IgnoreQueryFilters().Where(t => t.BucketId == id).ToListAsync(ct))
        {
            t.MoveBucket(id, target!.Id);
        }

        foreach (var r in await db.RecurringTransactions.Where(r => r.BucketId == id).ToListAsync(ct))
        {
            r.MoveBucket(id, target!.Id);
        }

        foreach (var account in await db.Accounts.Where(a => a.AllocationBucketId == id).ToListAsync(ct))
        {
            account.SetAllocationBucket(target?.Id);
        }

        foreach (var budget in await db.Budgets.Include(b => b.Items)
                     .Where(b => b.Items.Any(i => i.BucketId == id)).ToListAsync(ct))
        {
            budget.RemoveTargetsOf(id);
        }

        db.AllocationChecks.RemoveRange(await db.AllocationChecks.Where(c => c.BucketId == id).ToListAsync(ct));
        db.Buckets.Remove(bucket);
        await db.SaveChangesAsync(ct);
        return p;
    }
}
