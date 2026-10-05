using Finance.Application.Abstractions;
using Finance.Application.Goals;
using Finance.Domain.Interest;
using Finance.Domain.Recurring;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using SharedKernel.Notifications;

namespace Finance.Application.Notifications;

/// <summary>
/// Finance's part of the notification centre: recurring items due for confirmation, closed interest months still
/// estimated, and goals that reached their target. Read-only.
/// </summary>
public sealed class FinanceNotificationSource(IFinanceDb db) : INotificationSource
{
    /// <summary>Enough for any real backlog; the recurring page lists everything.</summary>
    public const int MaxPerKind = 50;

    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        var items = new List<NotificationItem>();
        items.AddRange(await RecurringAsync(today, ct));
        items.AddRange(await InterestAsync(today, ct));
        items.AddRange(await GoalsAsync(today, ct));
        return items;
    }

    private async Task<IEnumerable<NotificationItem>> RecurringAsync(DateOnly today, CancellationToken ct)
    {
        var due = await (
                from e in db.ExpectedTransactions.AsNoTracking()
                join r in db.RecurringTransactions.AsNoTracking() on e.RecurringTransactionId equals r.Id
                where e.Status == ExpectedStatus.Pending && e.DueOn <= today
                orderby e.DueOn, r.Name
                select new { e.Id, r.Name, r.Type, e.DueOn, e.Amount, e.Currency })
            .Take(MaxPerKind)
            .ToListAsync(ct);

        return due.Select(e => new NotificationItem(
            $"recurring:{e.Id}",
            NotificationKind.RecurringDue,
            e.DueOn < today ? NotificationSeverity.Warning : NotificationSeverity.Info,
            e.DueOn,
            "/recurring",
            e.Id,
            new Dictionary<string, object?>
            {
                ["name"] = e.Name, ["type"] = e.Type.ToString(), ["amount"] = e.Amount, ["currency"] = e.Currency,
                ["overdue"] = e.DueOn < today,
            },
            ["confirm", "skip"]));
    }

    private async Task<IEnumerable<NotificationItem>> InterestAsync(DateOnly today, CancellationToken ct)
    {
        var current = YearMonth.From(today);
        var months = await (
                from m in db.InterestMonths.AsNoTracking()
                join a in db.Accounts.AsNoTracking() on m.AccountId equals a.Id
                where m.Status == InterestMonthStatus.Estimated &&
                      (m.Year < current.Year || (m.Year == current.Year && m.Month < current.Month))
                orderby m.Year, m.Month, a.Name
                select new { m.Id, m.Year, m.Month, m.EstimatedAmount, AccountId = a.Id, a.Name, a.Currency })
            .Take(MaxPerKind)
            .ToListAsync(ct);

        return months.Select(m =>
        {
            var period = new YearMonth(m.Year, m.Month);
            return new NotificationItem(
                $"interest:{m.Id}",
                NotificationKind.InterestToReconcile,
                NotificationSeverity.Info,
                period.LastDay,
                "/accounts",
                m.Id,
                new Dictionary<string, object?>
                {
                    ["account"] = m.Name, ["accountId"] = m.AccountId, ["month"] = period.ToString(),
                    ["amount"] = m.EstimatedAmount, ["currency"] = m.Currency,
                },
                ["confirm"]);
        });
    }

    private async Task<IEnumerable<NotificationItem>> GoalsAsync(DateOnly today, CancellationToken ct) =>
        (await GoalEndpoints.ListAsync(db, today, includeArchived: false, ct))
        .Where(g => g.Achieved)
        .Take(MaxPerKind)
        .Select(g => new NotificationItem(
            $"goal:{g.Id}",
            NotificationKind.GoalReached,
            NotificationSeverity.Info,
            today,
            "/goals",
            g.Id,
            new Dictionary<string, object?> { ["name"] = g.Name, ["amount"] = g.TargetAmount, ["currency"] = Currency.Base },
            []));
}
