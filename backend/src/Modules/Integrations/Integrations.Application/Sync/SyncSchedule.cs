using Integrations.Application.Contracts;

namespace Integrations.Application.Sync;

/// <summary>The last scheduled-or-manual attempt for a connection, as far as the schedule cares.</summary>
public sealed record LastAttempt(DateTimeOffset StartedAtUtc, bool Failed, bool LockedOut);

/// <summary>
/// When a connection is due for a scheduled sync. A failed attempt backs off instead of retrying on every tick:
/// brokers count failed requests, and IBKR blocks the token after too many (Flex error 1025).
/// </summary>
public static class SyncSchedule
{
    public static readonly TimeSpan AfterLockout = TimeSpan.FromHours(12);

    public static TimeSpan AfterFailure(BrokerKind kind) =>
        kind == BrokerKind.InteractiveBrokers ? TimeSpan.FromHours(6) : TimeSpan.FromHours(2);

    public static bool IsDue(BrokerKind kind, DateTimeOffset? lastSuccess, LastAttempt? last, DateTimeOffset now)
    {
        if (last is { Failed: true })
        {
            var wait = last.LockedOut ? AfterLockout : AfterFailure(kind);
            if (now - last.StartedAtUtc < wait)
            {
                return false;
            }
        }

        var success = lastSuccess ?? DateTimeOffset.MinValue;
        return kind switch
        {
            BrokerKind.Trading212 or BrokerKind.Binance => now - success > TimeSpan.FromHours(4),
            // Flex data is end-of-day: one run per day, after 06:00 UTC.
            BrokerKind.InteractiveBrokers => now.Hour >= 6 && success.UtcDateTime.Date < now.UtcDateTime.Date,
            _ => now - success > TimeSpan.FromHours(24),
        };
    }

    /// <summary>Recognises a broker-side lockout in a stored job error.</summary>
    public static bool IsLockout(string? errors) =>
        errors is not null && (errors.Contains("1025", StringComparison.Ordinal) ||
                               errors.Contains("too many failed attempts", StringComparison.OrdinalIgnoreCase));
}
