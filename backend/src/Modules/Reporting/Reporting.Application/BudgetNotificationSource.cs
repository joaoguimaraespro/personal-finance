using Finance.Application.Abstractions;
using Reporting.Application.Queries;
using SharedKernel;
using SharedKernel.Notifications;

namespace Reporting.Application;

/// <summary>
/// Category limits this month that are exceeded ("over") or at 90 % or more ("near"), with the same figures and
/// thresholds as the budget page. Read-only like the rest of reporting.
/// </summary>
public sealed class BudgetNotificationSource(LedgerAggregates ledger, IFinanceDb db) : INotificationSource
{
    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        var period = YearMonth.From(today);
        var lines = await ReportEndpoints.CategoryBreakdownAsync(ledger, db, period, ct);
        return lines
            .Where(l => l.Status is "over" or "near")
            .OrderByDescending(l => l.Status == "over")
            .ThenByDescending(l => l.Budget is > 0 ? l.Actual / l.Budget.Value : 0)
            .Select(l =>
            {
                var over = l.Status == "over";
                return new NotificationItem(
                    // The status is part of the id: crossing from "near" to "over" is news again.
                    $"budget:{period}:{l.CategoryId}:{l.Status}",
                    over ? NotificationKind.BudgetOver : NotificationKind.BudgetNear,
                    over ? NotificationSeverity.Warning : NotificationSeverity.Info,
                    today,
                    "/budgets",
                    l.CategoryId,
                    new Dictionary<string, object?>
                    {
                        ["category"] = l.Name, ["categoryKey"] = l.Key, ["spent"] = l.Actual, ["budget"] = l.Budget,
                        ["currency"] = Currency.Base, ["month"] = period.ToString(),
                    },
                    []);
            })
            .ToList();
    }
}
