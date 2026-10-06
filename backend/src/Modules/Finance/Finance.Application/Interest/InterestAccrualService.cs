using Finance.Application.Abstractions;
using Finance.Domain;
using Finance.Domain.Accounts;
using Finance.Domain.Interest;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Finance.Application.Interest;

/// <summary>
/// Keeps exactly one estimated interest entry per account and month in the ledger (source
/// <see cref="DataSource.InterestEstimate"/>, category "interest"), recalculated from daily balances up to today.
/// Because the estimate is an ordinary income row, every balance, net-worth and report figure includes it without
/// special cases. A month never carries an estimate once it is settled — reconciled by the user, or real interest
/// for it is already in the ledger — so interest is never counted twice.
/// </summary>
public sealed class InterestAccrualService(IFinanceDb db, TimeProvider clock, ILogger<InterestAccrualService> logger)
{
    public static readonly Guid InterestCategoryId = SystemCatalog.CategoryId("interest");

    public static string ExternalIdFor(Guid accountId, YearMonth month) => $"interest:{accountId:N}:{month}";

    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Recalculates every account that has (or had) a rate. Used by the periodic job.</summary>
    public async Task RecalculateAllAsync(CancellationToken ct)
    {
        var ids = await db.InterestRates.Select(r => r.AccountId)
            .Union(db.InterestMonths.Select(m => m.AccountId))
            .Distinct()
            .ToListAsync(ct);
        await RecalculateAsync(ids.Select(id => (Guid?)id), ct);
    }

    /// <summary>Recalculates the given accounts; accounts without any rate history are ignored cheaply.</summary>
    public async Task RecalculateAsync(IEnumerable<Guid?> accountIds, CancellationToken ct)
    {
        var ids = accountIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var relevant = await db.InterestRates.Where(r => ids.Contains(r.AccountId)).Select(r => r.AccountId)
            .Union(db.InterestMonths.Where(m => ids.Contains(m.AccountId)).Select(m => m.AccountId))
            .Distinct()
            .ToListAsync(ct);
        foreach (var id in relevant)
        {
            await RecalculateAccountAsync(id, ct);
        }
    }

    /// <summary>
    /// Like <see cref="RecalculateAsync"/> but never fails the caller: used right after a ledger change that has
    /// already been saved. The periodic job retries anything that failed here.
    /// </summary>
    public async Task TryRecalculateAsync(IEnumerable<Guid?> accountIds, CancellationToken ct)
    {
        try
        {
            await RecalculateAsync(accountIds, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Interest recalculation deferred to the next scheduled run");
        }
    }

    private async Task RecalculateAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var today = Today;
        var rates = await db.InterestRates.AsNoTracking().Where(r => r.AccountId == accountId)
            .OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        var months = await db.InterestMonths.Where(m => m.AccountId == accountId).ToListAsync(ct);
        var estimates = await db.Transactions
            .Where(t => t.Source == DataSource.InterestEstimate && t.AccountId == accountId)
            .ToListAsync(ct);

        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Source != DataSource.InterestEstimate &&
                        (t.AccountId == accountId || t.CounterAccountId == accountId))
            .Select(t => new
            {
                t.OccurredOn, t.Type, t.AccountId, t.BaseAmount, t.OriginalAmount,
                // A split income with an interest line is real interest too.
                IsInterest = t.CategoryId == InterestCategoryId || t.Splits.Any(s => s.CategoryId == InterestCategoryId),
            })
            .ToListAsync(ct);

        var useBase = account.Currency == Currency.Base;
        var flows = new Dictionary<DateOnly, decimal>();
        foreach (var r in rows)
        {
            var amount = useBase ? r.BaseAmount : r.OriginalAmount;
            var signed = Signed(r.Type, r.AccountId == accountId, amount);
            flows[r.OccurredOn] = flows.GetValueOrDefault(r.OccurredOn) + signed;
        }

        // Real interest already in the ledger (bank export, manual entry, reconciliation) settles its month.
        var settled = rows
            .Where(r => r.AccountId == accountId && r.Type == TransactionType.Income && r.IsInterest)
            .Select(r => YearMonth.From(r.OccurredOn))
            .ToHashSet();
        settled.UnionWith(months.Where(m => m.IsResolved).Select(m => m.Period));

        var through = today;
        if (account.ArchivedAtUtc is { } archived)
        {
            var archivedOn = DateOnly.FromDateTime(archived.UtcDateTime).AddDays(-1);
            through = archivedOn < through ? archivedOn : through;
        }

        var accruals = rates.Count == 0
            ? []
            : InterestAccrual.Compute(new AccrualInput(account.OpeningBalance, account.OpeningBalanceOn, flows,
                rates.Select(r => r.ToPeriod()).ToList(), account.InterestPayout, through, settled));
        var wanted = accruals.Where(a => a.Net > 0).ToDictionary(a => a.Month);

        decimal? fxRate = useBase ? null : await LatestFxRateAsync(account.Currency, ct);
        if (!useBase && fxRate is null)
        {
            // Without any known EUR rate the estimate cannot be booked; keep what exists and try again later.
            logger.LogInformation("Interest estimate skipped: no exchange rate known for the account currency");
            return;
        }

        foreach (var estimate in estimates)
        {
            var month = YearMonth.From(estimate.OccurredOn);
            if (!wanted.ContainsKey(month) || estimate.ExternalId != ExternalIdFor(accountId, month))
            {
                db.Transactions.Remove(estimate);
            }
        }

        foreach (var (month, accrual) in wanted)
        {
            var draft = new TransactionDraft(TransactionType.Income, accrual.LastDay, accrual.Net, account.Currency,
                accountId, InterestCategoryId, FxRate: fxRate,
                Description: $"Estimated interest {month}",
                Notes: FormattableString.Invariant($"≈ {accrual.Gross} gross, {accrual.DaysAccrued} days"));
            var existing = estimates.FirstOrDefault(e => e.ExternalId == ExternalIdFor(accountId, month));
            Guid transactionId;
            if (existing is null)
            {
                var created = Transaction.Create(draft, DataSource.InterestEstimate,
                    externalId: ExternalIdFor(accountId, month));
                if (created.IsFailure)
                {
                    continue;
                }

                db.Transactions.Add(created.Value);
                transactionId = created.Value.Id;
            }
            else
            {
                if (existing.OriginalAmount != draft.Amount || existing.OccurredOn != draft.OccurredOn ||
                    existing.FxRate != (fxRate ?? 1m))
                {
                    existing.Update(draft);
                }

                transactionId = existing.Id;
            }

            var row = months.FirstOrDefault(m => m.Period == month);
            if (row is null)
            {
                row = InterestMonth.Start(accountId, month, now);
                db.InterestMonths.Add(row);
                months.Add(row);
            }

            row.UpdateEstimate(accrual.Net, accrual.Gross, transactionId);
        }

        // Months that no longer carry an estimate (settled by real interest, rate removed, balance went to zero).
        foreach (var stale in months.Where(m => !m.IsResolved && !wanted.ContainsKey(m.Period)))
        {
            db.InterestMonths.Remove(stale);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Latest EUR rate used by any entry in that currency (estimates are bookkeeping, not quotes).</summary>
    public async Task<decimal?> LatestFxRateAsync(string currency, CancellationToken ct) =>
        await db.Transactions.AsNoTracking()
            .Where(t => t.OriginalCurrency == currency && t.Source != DataSource.InterestEstimate)
            .OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => (decimal?)t.FxRate)
            .FirstOrDefaultAsync(ct);

    /// <summary>Same direction rules as <c>AccountBalances</c>.</summary>
    private static decimal Signed(TransactionType type, bool isAccount, decimal amount)
    {
        var inflow = type is TransactionType.Income or TransactionType.InvestmentSale;
        if (isAccount)
        {
            return inflow ? amount : -amount;
        }

        return type == TransactionType.InvestmentSale ? -amount : amount;
    }
}
