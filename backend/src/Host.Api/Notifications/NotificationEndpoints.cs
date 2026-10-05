using SharedKernel.Notifications;

namespace Host.Api.Notifications;

public sealed record NotificationsDto(IReadOnlyList<NotificationItem> Items, bool Partial, DateTimeOffset GeneratedAtUtc);

/// <summary>
/// The notification centre: one read-only endpoint that asks every module's <see cref="INotificationSource"/> what
/// needs the owner's attention. Modules stay independent — the host only composes their answers. Nothing is stored:
/// an item disappears as soon as the underlying situation is resolved, and "seen" lives in the browser.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder api)
    {
        api.MapGet("/notifications", async (IEnumerable<INotificationSource> sources, TimeProvider clock,
                ILoggerFactory logs, CancellationToken ct) => Results.Ok(await CollectAsync(sources, clock, logs, ct)))
            .WithTags("Notifications");
        return api;
    }

    public static async Task<NotificationsDto> CollectAsync(IEnumerable<INotificationSource> sources,
        TimeProvider clock, ILoggerFactory logs, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var items = new List<NotificationItem>();
        var partial = false;

        // Sequential on purpose: sources share scoped DbContexts, which allow one query at a time.
        foreach (var source in sources)
        {
            try
            {
                items.AddRange(await source.GetAsync(today, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One module failing must not hide what the others report.
                partial = true;
                logs.CreateLogger(typeof(NotificationEndpoints)).LogWarning(ex,
                    "Notification source {Source} failed", source.GetType().Name);
            }
        }

        var ordered = items
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Date)
            .ToList();
        return new NotificationsDto(ordered, partial, now);
    }
}
