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
        items.AddRange(await LoansAsync(today, ct));
        return items;
    }

    /// <summary>A rate revision is announced this many days ahead (banks send the new rate around then).</summary>
    public const int RevisionNoticeDays = 14;

    private async Task<IEnumerable<NotificationItem>> LoansAsync(DateOnly today, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking().Include(l => l.Rates).Include(l => l.Prepayments).ToListAsync(ct);
        if (loans.Count == 0)
        {
            return [];
        }

        var ids = loans.Select(l => l.AccountId).ToList();
        var names = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id) && a.ArchivedAtUtc == null)
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var items = new List<NotificationItem>();
        foreach (var loan in loans.Where(l => names.ContainsKey(l.AccountId)))
        {
            var name = names[loan.AccountId];
            // Only the oldest: booking it brings up the next one.
            var pending = loan.PendingInstalments(today);
            if (pending.Count > 0 && pending[0] is var due)
            {
                items.Add(new NotificationItem(
                    $"loan:{loan.Id}:{due.Number}",
                    NotificationKind.LoanInstalmentDue,
                    due.Date < today ? NotificationSeverity.Warning : NotificationSeverity.Info,
                    due.Date,
                    $"/loans/{loan.AccountId}",
                    loan.AccountId,
                    new Dictionary<string, object?>
                    {
                        ["name"] = name, ["number"] = due.Number, ["amount"] = due.Payment,
                        ["interest"] = due.Interest, ["capital"] = due.Principal, ["currency"] = Currency.Base,
                        ["overdue"] = due.Date < today,
                    },
                    ["confirm", "skip"]));
            }

            // A revision date (soon or past) with no rate entered for it.
            var revision = loan.RevisionDatesUntil(today.AddDays(RevisionNoticeDays)).LastOrDefault();
            if (revision != default && loan.Rates.All(r => r.EffectiveFrom != revision))
            {
                items.Add(new NotificationItem(
                    $"loan-revision:{loan.Id}:{revision:yyyy-MM-dd}",
                    NotificationKind.LoanRateRevision,
                    revision <= today ? NotificationSeverity.Warning : NotificationSeverity.Info,
                    revision,
                    $"/loans/{loan.AccountId}",
                    loan.AccountId,
                    new Dictionary<string, object?> { ["name"] = name, ["index"] = loan.IndexName },
                    []));
            }
        }

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
