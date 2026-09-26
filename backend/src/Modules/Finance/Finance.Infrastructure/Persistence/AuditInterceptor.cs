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

        var audits = new List<TransactionAudit>();
        foreach (var entry in context.ChangeTracker.Entries<Transaction>())
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                EntityState.Modified => ModifiedAction(entry),
                _ => (AuditAction?)null,
            };
            if (action is null)
            {
                continue;
            }

            audits.Add(new TransactionAudit
            {
                TransactionId = entry.Entity.Id,
                Action = action.Value,
                AtUtc = now,
                Actor = currentUser.Actor,
                Changes = Serialize(entry, action.Value),
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

    private static string Serialize(EntityEntry<Transaction> entry, AuditAction action)
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

        return JsonSerializer.Serialize(changes);
    }
}
