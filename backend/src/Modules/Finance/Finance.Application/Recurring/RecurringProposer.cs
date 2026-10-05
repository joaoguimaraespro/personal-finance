using Finance.Application.Abstractions;
using Finance.Domain.Recurring;
using Microsoft.EntityFrameworkCore;

namespace Finance.Application.Recurring;

/// <summary>
/// Turns due recurring templates into <see cref="ExpectedTransaction"/> proposals. It never creates ledger
/// entries: every proposal waits for the user to confirm, edit or skip it.
/// </summary>
public sealed class RecurringProposer(IFinanceDb db, TimeProvider clock)
{
    /// <summary>How far ahead proposals are generated, so upcoming bills are visible before they are due.</summary>
    public const int LookaheadDays = 7;

    /// <summary>The date schedules are evaluated against (UTC calendar day, like the rest of the app).</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<int> ProposeDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var horizon = DateOnly.FromDateTime(now.UtcDateTime).AddDays(LookaheadDays);
        var templates = await db.RecurringTransactions
            .Where(r => r.IsActive && r.NextDueOn <= horizon)
            .ToListAsync(ct);

        var created = 0;
        foreach (var template in templates)
        {
            var dates = template.TakeDueOccurrences(horizon);
            var existing = await db.ExpectedTransactions
                .Where(e => e.RecurringTransactionId == template.Id && dates.Contains(e.DueOn))
                .Select(e => e.DueOn)
                .ToListAsync(ct);
            foreach (var date in dates.Except(existing))
            {
                db.ExpectedTransactions.Add(ExpectedTransaction.Propose(template, date, now));
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        return created;
    }
}
