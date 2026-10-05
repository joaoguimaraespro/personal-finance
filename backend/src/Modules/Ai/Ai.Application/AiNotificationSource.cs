using Ai.Application.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Notifications;

namespace Ai.Application;

/// <summary>
/// One item per AI client that changed data in the last 24 hours (ADR-0008), so the owner can review the audit log
/// or restore a deletion from the recycle bin. Only counts and the client's name are exposed.
/// </summary>
public sealed class AiNotificationSource(IAiDb db, TimeProvider clock) : INotificationSource
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - Window;
        var writes = await db.AuditEvents.AsNoTracking()
            .Where(e => e.Write && e.Decision == AiDecision.Allowed && e.AtUtc >= since)
            .OrderByDescending(e => e.Id)
            .Take(1000)
            .Select(e => new { e.Id, e.ClientId, e.ClientName, e.Tool, e.AtUtc })
            .ToListAsync(ct);

        return writes
            .GroupBy(e => e.ClientId)
            .Select(g =>
            {
                var latest = g.First();
                return new NotificationItem(
                    // A further write by the same client makes the item new again.
                    $"ai:{g.Key?.ToString() ?? "unknown"}:{latest.Id}",
                    NotificationKind.AiWrites,
                    NotificationSeverity.Info,
                    DateOnly.FromDateTime(latest.AtUtc.UtcDateTime),
                    "/ai",
                    g.Key,
                    new Dictionary<string, object?>
                    {
                        ["client"] = latest.ClientName, ["count"] = g.Count(),
                        ["deleted"] = g.Count(e => e.Tool.StartsWith("delete_", StringComparison.Ordinal)),
                        ["at"] = latest.AtUtc,
                    },
                    []);
            })
            .ToList();
    }
}
