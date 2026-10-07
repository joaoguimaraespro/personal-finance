using Finance.Application.Abstractions;
using Finance.Domain.Allocation;
using Microsoft.EntityFrameworkCore;
using Reporting.Application.Queries;
using SharedKernel;
using SharedKernel.Notifications;

namespace Reporting.Application;

/// <summary>
/// Where a bucket's allocation stands from what was actually set aside: nothing yet, part of it, or all of it.
/// A status the owner picked by hand (the monthly checklist) always wins over this.
/// </summary>
public static class AllocationProgress
{
    /// <summary><c>null</c> when there is nothing to set aside (no target, or a target of zero).</summary>
    public static AllocationStatus? Of(decimal? target, decimal actual) => target switch
    {
        null or <= 0 => null,
        _ when actual >= target => AllocationStatus.Done,
        _ when actual > 0 => AllocationStatus.Partial,
        _ => AllocationStatus.Todo,
    };
}

/// <summary>
/// This month's allocations still to do, like a recurring item waiting to be confirmed: once income has arrived,
/// every bucket with a target that is not fully set aside (and not marked done or not applicable by hand) shows up,
/// with the amount left. Read-only like the rest of reporting.
/// </summary>
public sealed class AllocationNotificationSource(LedgerAggregates ledger, IFinanceDb db) : INotificationSource
{
    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        var period = YearMonth.From(today);
        var month = (await ledger.MonthlySummariesAsync(period, period, ct))[^1];
        if (month.Income <= 0)
        {
            return []; // targets are a share of income: nothing to set aside before it arrives
        }

        var manual = await db.AllocationChecks.AsNoTracking()
            .Where(c => c.Year == period.Year && c.Month == period.Month)
            .ToDictionaryAsync(c => c.BucketId, c => c.Status, ct);

        return month.Buckets
            .Select(b => (Line: b, Status: AllocationProgress.Of(b.Target, b.Actual)))
            .Where(x => x.Status is AllocationStatus.Todo or AllocationStatus.Partial &&
                        manual.GetValueOrDefault(x.Line.BucketId, AllocationStatus.Todo)
                            is AllocationStatus.Todo or AllocationStatus.Partial)
            .Select(x =>
            {
                var remaining = x.Line.Target!.Value - x.Line.Actual;
                return new NotificationItem(
                    // Partial is news again: the amount left changed.
                    $"allocation:{period}:{x.Line.BucketId}:{x.Status}",
                    NotificationKind.AllocationDue,
                    NotificationSeverity.Info,
                    today,
                    $"/monthly?period={period}",
                    x.Line.BucketId,
                    new Dictionary<string, object?>
                    {
                        ["bucket"] = x.Line.Name, ["investment"] = x.Line.IsInvestment, ["month"] = period.ToString(),
                        ["target"] = x.Line.Target, ["actual"] = x.Line.Actual, ["remaining"] = remaining,
                        ["partial"] = x.Status == AllocationStatus.Partial, ["currency"] = Currency.Base,
                    },
                    ["record", "skip"]);
            })
            .ToList();
    }
}
