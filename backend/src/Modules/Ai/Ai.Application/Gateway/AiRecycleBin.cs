using Ai.Application.Domain;
using Finance.Application.Abstractions;
using Finance.Application.Interest;
using Finance.Application.Transactions;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Ai.Application.Gateway;

public sealed record RecycledItemDto(Guid Id, string Kind, Guid RecordId, string ClientName, string Summary,
    DateTimeOffset DeletedAtUtc, DateTimeOffset PurgeAfterUtc);

/// <summary>
/// What AI clients deleted (ADR-0008). Deletes are soft: the owner restores an item with one click for 30 days;
/// after that <see cref="PurgeAsync"/> (run by a background job) removes it for good.
/// </summary>
public sealed class AiRecycleBin(IAiDb ai, IFinanceDb finance, InterestAccrualService interest, TimeProvider clock,
    ILogger<AiRecycleBin> logger)
{
    public static readonly Error NotFound = Error.NotFound("RecycleBin.NotFound", "Not in the recycle bin.");

    public async Task<IReadOnlyList<RecycledItemDto>> ListAsync(CancellationToken ct) =>
        await ai.RecycleBin.AsNoTracking()
            .Where(i => i.RestoredAtUtc == null && i.PurgedAtUtc == null)
            .OrderByDescending(i => i.DeletedAtUtc)
            .Take(200)
            .Select(i => new RecycledItemDto(i.Id, i.Kind, i.RecordId, i.ClientName, i.Summary, i.DeletedAtUtc,
                i.PurgeAfterUtc))
            .ToListAsync(ct);

    public async Task<Result> RestoreAsync(Guid id, CancellationToken ct)
    {
        var item = await ai.RecycleBin.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null || !item.InBin)
        {
            return NotFound;
        }

        var restored = await TransactionCommands.RestoreAsync(finance, interest, item.RecordId, ct);
        if (restored.IsFailure && restored.Error != TransactionErrors.NotFound)
        {
            return restored.Error;
        }

        // Not found as deleted means it was already restored elsewhere (e.g. from the transaction's history).
        item.MarkRestored(clock.GetUtcNow());
        await ai.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Removes for good what AI deleted more than 30 days before <paramref name="now"/>, unless it was restored or
    /// deleted again since (then it is no longer this deletion). Also forgets idempotency receipts older than a day.
    /// </summary>
    public async Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await ai.RecycleBin
            .Where(i => i.RestoredAtUtc == null && i.PurgedAtUtc == null && i.PurgeAfterUtc <= now)
            .OrderBy(i => i.PurgeAfterUtc).Take(500).ToListAsync(ct);
        var purged = 0;
        foreach (var item in due)
        {
            var transaction = await finance.Transactions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == item.RecordId, ct);
            if (transaction is null)
            {
                item.MarkPurged(now);
                continue;
            }

            if (transaction.DeletedAtUtc is not { } deletedAt || (deletedAt - item.DeletedAtUtc).Duration() > TimeSpan.FromSeconds(1))
            {
                item.MarkRestored(now);
                continue;
            }

            finance.Transactions.Remove(transaction);
            item.MarkPurged(now);
            purged++;
        }

        await finance.SaveChangesAsync(ct);
        var receiptsBefore = now.AddHours(-AiWriteReceipt.RetentionHours);
        await ai.WriteReceipts.Where(r => r.AtUtc < receiptsBefore).ExecuteDeleteAsync(ct);
        await ai.SaveChangesAsync(ct);
        if (purged > 0)
        {
            logger.LogInformation("AI recycle bin: {Count} item(s) purged after {Days} days", purged, AiRecycledItem.RetentionDays);
        }

        return purged;
    }
}
