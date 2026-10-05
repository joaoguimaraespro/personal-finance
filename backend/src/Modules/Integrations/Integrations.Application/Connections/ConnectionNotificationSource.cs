using Microsoft.EntityFrameworkCore;
using SharedKernel.Notifications;

namespace Integrations.Application.Connections;

/// <summary>
/// Broker connections that need the owner: credentials rejected or locked (e.g. IBKR Flex error 1025), the last
/// sync failed, or credentials that expire within <see cref="ExpiryWarningDays"/> days. Disabled connections stay
/// quiet. Only the stored, credential-free error message is exposed.
/// </summary>
public sealed class ConnectionNotificationSource(IIntegrationsDb db) : INotificationSource
{
    public const int ExpiryWarningDays = 14;

    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        var connections = await db.Connections.AsNoTracking()
            .Where(c => c.Status != ConnectionStatus.Disabled)
            .OrderBy(c => c.DisplayName)
            .ToListAsync(ct);
        var ids = connections.Select(c => c.Id).ToList();
        var lastJobs = await db.SyncJobs.AsNoTracking()
            .Where(j => ids.Contains(j.ConnectionId) && j.Outcome != SyncOutcome.Running)
            .GroupBy(j => j.ConnectionId)
            .Select(g => g.OrderByDescending(j => j.StartedAtUtc).First())
            .ToListAsync(ct);

        var items = new List<NotificationItem>();
        foreach (var c in connections)
        {
            var job = lastJobs.FirstOrDefault(j => j.ConnectionId == c.Id);
            var (reason, severity) = c switch
            {
                { Status: ConnectionStatus.NeedsAttention } => ("needsAttention", NotificationSeverity.Error),
                { LastError: not null } => ("syncFailed", NotificationSeverity.Warning),
                _ when job?.Outcome == SyncOutcome.Failed => ("syncFailed", NotificationSeverity.Warning),
                { CredentialsExpireOn: { } on } when on < today => ("expired", NotificationSeverity.Error),
                { CredentialsExpireOn: { } on } when on <= today.AddDays(ExpiryWarningDays) =>
                    ("expiring", NotificationSeverity.Warning),
                _ => (null, NotificationSeverity.Info),
            };
            if (reason is null)
            {
                continue;
            }

            var finished = job?.FinishedAtUtc ?? DateTimeOffset.MinValue;
            var at = c.UpdatedAtUtc > finished ? c.UpdatedAtUtc : finished;
            if (at == DateTimeOffset.MinValue)
            {
                at = c.CreatedAtUtc == default ? new DateTimeOffset(today, TimeOnly.MinValue, TimeSpan.Zero) : c.CreatedAtUtc;
            }
            items.Add(new NotificationItem(
                // A different error (or reason) is news again; the same one repeating every sync is not.
                $"broker:{c.Id}:{reason}:{NotificationIds.Fingerprint(c.LastError)}",
                NotificationKind.BrokerAttention,
                severity,
                reason is "expired" or "expiring" ? c.CredentialsExpireOn!.Value : DateOnly.FromDateTime(at.UtcDateTime),
                "/connections",
                c.Id,
                new Dictionary<string, object?>
                {
                    ["name"] = c.DisplayName, ["broker"] = c.Kind.ToString(), ["reason"] = reason,
                    ["error"] = c.LastError, ["expiresOn"] = c.CredentialsExpireOn,
                },
                []));
        }

        return items;
    }
}
