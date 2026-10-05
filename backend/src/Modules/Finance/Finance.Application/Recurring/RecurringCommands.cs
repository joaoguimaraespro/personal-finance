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

    public static async Task<Result<RecurringTransaction>> UpdateAsync(IFinanceDb db, Guid id,
        RecurringDefinition definition, CancellationToken ct)
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

        recurring.Update(definition);
        await db.SaveChangesAsync(ct);
        return recurring;
    }

    /// <summary>Turns a pending proposal into a real transaction (source Recurring), optionally adjusted.</summary>
    public static async Task<Result<Transaction>> ConfirmAsync(IFinanceDb db, Guid id, ConfirmExpectedRequest? req,
        DateTimeOffset now, CancellationToken ct)
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

        var resolved = await TransactionReferences.ResolveAsync(db, draft, ct);
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
            FxRate: def.Currency == Currency.Base ? null : 1m);
        var resolved = await TransactionReferences.ResolveAsync(db, probe, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var probeResult = Transaction.Create(resolved.Value, DataSource.Recurring);
        return probeResult.IsFailure ? probeResult.Error : null;
    }
}
