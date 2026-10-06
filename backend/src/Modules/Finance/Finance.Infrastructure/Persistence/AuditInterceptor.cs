using System.Globalization;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel;

namespace Finance.Infrastructure.Persistence;

/// <summary>
/// Stamps created/updated times and writes an append-only <see cref="TransactionAudit"/> row for every change to a
/// transaction, whichever code path made it (UI, import, recurring confirmation).
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            Stamp(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Stamp(context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }

        // Split lines are owned rows: a change to them alone leaves the transaction itself unmodified.
        var linesBefore = SplitLinesBefore(context);

        var audits = new List<TransactionAudit>();
        foreach (var entry in context.ChangeTracker.Entries<Transaction>())
        {
            var splitsChanged = linesBefore.TryGetValue(entry.Entity.Id, out var before) &&
                                Describe(entry.Entity.Splits) != before;
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                EntityState.Modified => ModifiedAction(entry) ?? (splitsChanged ? AuditAction.Updated : null),
                EntityState.Unchanged when splitsChanged => AuditAction.Updated,
                _ => (AuditAction?)null,
            };
            if (action is null)
            {
                continue;
            }

            if (action == AuditAction.Updated && splitsChanged)
            {
                entry.Entity.UpdatedAtUtc = now;
            }

            audits.Add(new TransactionAudit
            {
                TransactionId = entry.Entity.Id,
                Action = action.Value,
                AtUtc = now,
                Actor = currentUser.Actor,
                Changes = Serialize(entry, action.Value, splitsChanged, before),
            });
        }

        context.Set<TransactionAudit>().AddRange(audits);
    }

    private static AuditAction? ModifiedAction(EntityEntry<Transaction> entry)
    {
        var deleted = entry.Property(t => t.DeletedAtUtc);
        if (deleted.IsModified)
        {
            return deleted.CurrentValue is null ? AuditAction.Restored : AuditAction.Deleted;
        }

        return entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(Transaction.UpdatedAtUtc))
            ? AuditAction.Updated
            : null;
    }

    /// <summary>
    /// For each transaction whose split lines are being added, changed or removed in this save: its lines as they
    /// were (original values of every line that existed before this save).
    /// </summary>
    private static Dictionary<Guid, string?> SplitLinesBefore(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<TransactionSplit>().ToList();
        var changedOwners = entries
            .Where(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
            .Select(OwnerId).ToHashSet();
        return changedOwners.ToDictionary(id => id, id => Describe(entries
            .Where(e => OwnerId(e) == id && e.State != EntityState.Added)
            .Select(e => (
                (int)e.Property(nameof(TransactionSplit.Position)).OriginalValue!,
                (Guid)e.Property(nameof(TransactionSplit.CategoryId)).OriginalValue!,
                (decimal)e.Property(nameof(TransactionSplit.OriginalAmount)).OriginalValue!))));
    }

    private static Guid OwnerId(EntityEntry e) =>
        (Guid)(e.Property("TransactionId").CurrentValue ?? e.Property("TransactionId").OriginalValue)!;

    private static string? Describe(IEnumerable<TransactionSplit> lines) =>
        Describe(lines.Select(l => (l.Position, l.CategoryId, l.OriginalAmount)));

    /// <summary>Lines as one readable audit value, "amount category-id; …" in order; null when not split.</summary>
    private static string? Describe(IEnumerable<(int Position, Guid CategoryId, decimal Amount)> lines)
    {
        var ordered = lines.OrderBy(l => l.Position).ToList();
        return ordered.Count == 0
            ? null
            : string.Join("; ", ordered.Select(l =>
                $"{l.Amount.ToString("0.00##", CultureInfo.InvariantCulture)} {l.CategoryId}"));
    }

    private static string Serialize(EntityEntry<Transaction> entry, AuditAction action, bool splitsChanged,
        string? splitsBefore)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            var name = p.Metadata.Name;
            if (name is nameof(Transaction.CreatedAtUtc) or nameof(Transaction.UpdatedAtUtc))
            {
                continue;
            }

            if (action == AuditAction.Created)
            {
                changes[name] = new { to = p.CurrentValue };
            }
            else if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
            {
                changes[name] = new { from = p.OriginalValue, to = p.CurrentValue };
            }
        }

        var splitsNow = Describe(entry.Entity.Splits);
        if (action == AuditAction.Created && splitsNow is not null)
        {
            changes[nameof(Transaction.Splits)] = new { to = splitsNow };
        }
        else if (action == AuditAction.Updated && splitsChanged)
        {
            // "—" means the split was removed: the transaction has a single category again.
            changes[nameof(Transaction.Splits)] = new { from = splitsBefore, to = splitsNow ?? "—" };
        }

        return JsonSerializer.Serialize(changes);
    }
}
