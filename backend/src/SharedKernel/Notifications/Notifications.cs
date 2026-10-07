using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Notifications;

/// <summary>What a notification is about. The web client picks the icon, wording and actions from it.</summary>
public enum NotificationKind
{
    /// <summary>A recurring item is due (or overdue) and waits to be confirmed or skipped.</summary>
    RecurringDue = 0,

    /// <summary>A closed month of savings interest still carries an estimate.</summary>
    InterestToReconcile = 1,

    /// <summary>A broker connection needs new credentials or its last sync failed.</summary>
    BrokerAttention = 2,

    /// <summary>A category spent more than its budget limit this month.</summary>
    BudgetOver = 3,

    /// <summary>A category reached 90 % of its budget limit this month.</summary>
    BudgetNear = 4,

    /// <summary>A goal reached its target and is still active.</summary>
    GoalReached = 5,

    /// <summary>An AI client changed data in the last 24 hours.</summary>
    AiWrites = 6,

    /// <summary>This month's income is in but a bucket's allocation (investments, savings) is not done yet.</summary>
    AllocationDue = 7,
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Something that needs the owner's attention, computed from existing data: it disappears once resolved.
/// <para><see cref="Id"/> is stable while the situation is the same, so the client can remember what it has seen;
/// it changes when the situation does (e.g. a new sync error), which makes the item new again.</para>
/// <para><see cref="Args"/> carries display values only (names, amounts) — never credentials. <see cref="Actions"/>
/// name inline actions the client maps to existing endpoints for <see cref="TargetId"/> (e.g. <c>confirm</c> and
/// <c>skip</c> for a recurring item).</para>
/// </summary>
public sealed record NotificationItem(
    string Id,
    NotificationKind Kind,
    NotificationSeverity Severity,
    DateOnly Date,
    string Link,
    Guid? TargetId,
    IReadOnlyDictionary<string, object?> Args,
    IReadOnlyList<string> Actions);

/// <summary>A module's contribution to the notification centre. Read-only: sources never change data.</summary>
public interface INotificationSource
{
    Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct);
}

public static class NotificationIds
{
    /// <summary>A short, process-independent fingerprint (unlike <see cref="string.GetHashCode()"/>).</summary>
    public static string Fingerprint(string? text) => string.IsNullOrEmpty(text)
        ? "0"
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
}
