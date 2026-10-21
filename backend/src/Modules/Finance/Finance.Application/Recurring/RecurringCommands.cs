using Finance.Application.Abstractions;
using Finance.Application.Transactions;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Recurring;

/// <summary>Recurring templates and their proposals: shared by the HTTP endpoints and the AI write tools.</summary>
public static class RecurringCommands
{
    public static async Task<Result<RecurringTransaction>> CreateAsync(IFinanceDb db, RecurringProposer proposer,
        RecurringDefinition definition, CancellationToken ct)
    {
        if (await ValidateReferencesAsync(db, definition, ct) is { } error)
        {
            return error;
        }

        var recurring = RecurringTransaction.Create(definition);
        db.RecurringTransactions.Add(recurring);
        await db.SaveChangesAsync(ct);
        await proposer.ProposeDueAsync(ct);
        return recurring;
    }

    /// <summary>
    /// Edits a template. When its schedule changes, pending proposals dated today or later are dropped and proposed
    /// again from the new anchor; confirmed and skipped ones, and overdue pending ones, stay as they are.
    /// </summary>
    public static async Task<Result<RecurringTransaction>> UpdateAsync(IFinanceDb db, RecurringProposer proposer,
        Guid id, RecurringDefinition definition, CancellationToken ct)
    {
        var recurring = await db.RecurringTransactions.FindAsync([id], ct);
        if (recurring is null)
        {
            return RecurringEndpoints.NotFound;
        }

        if (await ValidateReferencesAsync(db, definition, ct) is { } error)
        {
            return error;
        }

        var today = proposer.Today;
        var scheduleChanged = recurring.Update(definition, today);
        if (scheduleChanged)
        {
            var stale = await db.ExpectedTransactions
                .Where(e => e.RecurringTransactionId == id && e.Status == ExpectedStatus.Pending && e.DueOn >= today)
                .ToListAsync(ct);
            db.ExpectedTransactions.RemoveRange(stale);
        }

        await db.SaveChangesAsync(ct);
        if (scheduleChanged)
        {
            await proposer.ProposeDueAsync(ct);
        }

        return recurring;
    }

    /// <summary>Turns a pending proposal into a real transaction (source Recurring), optionally adjusted.</summary>
    /// <param name="cardPayment">Paid with a broker's card, from that broker account (see BrokerMovements).</param>
    public static async Task<Result<Transaction>> ConfirmAsync(IFinanceDb db, Guid id, ConfirmExpectedRequest? req,
        DateTimeOffset now, CancellationToken ct, bool cardPayment = false)
    {
        var item = await db.ExpectedTransactions.FindAsync([id], ct);
        if (item is null)
        {
            return RecurringEndpoints.ExpectedNotFound;
        }

        if (item.Status != ExpectedStatus.Pending)
        {
            return RecurringEndpoints.AlreadyResolved;
        }

        var template = await db.RecurringTransactions.AsNoTracking()
            .FirstAsync(r => r.Id == item.RecurringTransactionId, ct);
        var draft = template.ToDraft(req?.OccurredOn ?? item.DueOn, req?.Amount ?? item.Amount) with
        {
            AccountId = req?.AccountId ?? template.AccountId,
            FxRate = req?.FxRate,
            Notes = req?.Notes,
        };

        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct, cardPayment);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var created = Transaction.Create(resolved.Value, DataSource.Recurring, expectedTransactionId: item.Id);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Transactions.Add(created.Value);
        // The people who share it owe their part of what was actually paid (archived people no longer share).
        var people = template.Shares.Select(s => s.PersonId).ToList();
        var active = people.Count == 0
            ? []
            : await db.Accounts.Where(a => people.Contains(a.Id) && a.ArchivedAtUtc == null).Select(a => a.Id)
                .ToListAsync(ct);
        var shares = template.SharesFor(created.Value.OriginalAmount)
            .Where(s => active.Contains(s.PersonId))
            .Select(s => new ShareRequest(s.PersonId, s.Amount)).ToList();
        if (shares.Count > 0 && await TransactionShares.SyncAsync(db, created.Value, shares, now, ct) is { } shareError)
        {
            return shareError;
        }

        item.Confirm(created.Value.Id, now);
        await db.SaveChangesAsync(ct);
        return created.Value;
    }

    public static async Task<Result<ExpectedTransaction>> SkipAsync(IFinanceDb db, Guid id, DateTimeOffset now,
        CancellationToken ct)
    {
        var item = await db.ExpectedTransactions.FindAsync([id], ct);
        if (item is null)
        {
            return RecurringEndpoints.ExpectedNotFound;
        }

        if (item.Status != ExpectedStatus.Pending)
        {
            return RecurringEndpoints.AlreadyResolved;
        }

        item.Skip(now);
        await db.SaveChangesAsync(ct);
        return item;
    }

    private static async Task<Error?> ValidateReferencesAsync(IFinanceDb db, RecurringDefinition def,
        CancellationToken ct)
    {
        var probe = new TransactionDraft(def.Type, def.StartOn, def.Amount, def.Currency, def.AccountId,
            def.CategoryId, def.Nature, def.CounterAccountId, def.BucketId,
            FxRate: def.Currency == Currency.Base ? null : 1m, Splits: def.Splits);
        var resolved = await TransactionReferences.ResolveAsync(db, probe, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var probeResult = Transaction.Create(resolved.Value, DataSource.Recurring);
        if (probeResult.IsFailure)
        {
            return probeResult.Error;
        }

        if (def.Shares is not { Count: > 0 } shares)
        {
            return null;
        }

        // Shared with people (ADR-0011): checked like a shared expense of the template's amount.
        if (shares.Any(s => s.Percent is <= 0 or > 100) || shares.Sum(s => s.Percent) > 100)
        {
            return ShareErrors.InvalidAmount;
        }

        return await TransactionShares.ValidateAsync(db, resolved.Value,
            shares.Select(s => new ShareRequest(s.PersonId, 0.01m)).ToList(), ct);
    }
}
