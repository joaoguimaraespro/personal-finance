using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Transactions;

/// <summary>A person's part of a shared expense, in the expense's currency.</summary>
public sealed record ShareRequest(Guid PersonId, decimal Amount);

/// <summary>A share as shown on its expense.</summary>
public sealed record ShareDto(Guid TransactionId, Guid PersonId, string PersonName, decimal Amount);

public static class ShareErrors
{
    public static readonly Error NotShareable = Error.Validation("Transaction.Shares",
        "Only an expense with one category, paid from your own account, can be shared.");

    public static readonly Error InvalidPerson = Error.Validation("Transaction.Shares.Person",
        "Each share needs a different person.");

    public static readonly Error InvalidAmount = Error.Validation("Transaction.Shares.Amount",
        "Each share must be positive, and together they cannot exceed the expense.");

    public static readonly Error EditTheExpense = Error.Validation("Transaction.Share",
        "A share follows its expense: edit the expense, or delete the share to take that part on.");
}

/// <summary>
/// Keeps the people's shares of an expense in step with it (ADR-0011): each share is a refund of the expense's
/// category in the person's account, so spending counts only the owner's part.
/// </summary>
public static class TransactionShares
{
    public static async Task<Error?> ValidateAsync(IFinanceDb db, TransactionDraft expense,
        IReadOnlyList<ShareRequest> shares, CancellationToken ct)
    {
        if (shares.Count == 0)
        {
            return null;
        }

        if (expense.Type != TransactionType.Expense || expense.IsRefund || expense.Splits is { Count: > 0 })
        {
            return ShareErrors.NotShareable;
        }

        var payer = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == expense.AccountId, ct);
        if (payer is null || payer.Kind == AccountKind.Person)
        {
            return ShareErrors.NotShareable;
        }

        var ids = shares.Select(s => s.PersonId).ToList();
        var people = await db.Accounts.AsNoTracking()
            .CountAsync(a => ids.Contains(a.Id) && a.Kind == AccountKind.Person && a.ArchivedAtUtc == null, ct);
        if (ids.Distinct().Count() != ids.Count || people != ids.Count)
        {
            return ShareErrors.InvalidPerson;
        }

        return shares.Any(s => s.Amount <= 0 || decimal.Round(s.Amount, 2) != s.Amount) ||
               shares.Sum(s => s.Amount) > expense.Amount
            ? ShareErrors.InvalidAmount
            : null;
    }

    /// <summary>
    /// Brings the shares of <paramref name="expense"/> in line: with <paramref name="wanted"/> they become exactly
    /// those; without, the current ones keep their amounts and follow the expense's day, category and currency.
    /// </summary>
    public static async Task<Error?> SyncAsync(IFinanceDb db, Transaction expense, IReadOnlyList<ShareRequest>? wanted,
        DateTimeOffset now, CancellationToken ct)
    {
        var current = await db.Transactions.Where(t => t.SharedFromId == expense.Id).ToListAsync(ct);
        var target = wanted ?? current.Select(s => new ShareRequest(s.AccountId, s.OriginalAmount)).ToList();
        if (target.Count > 0 && (expense.Type != TransactionType.Expense || expense.IsRefund || expense.IsSplit ||
                                 target.Sum(s => s.Amount) > expense.OriginalAmount))
        {
            return wanted is null && target.Sum(s => s.Amount) > expense.OriginalAmount
                ? ShareErrors.InvalidAmount
                : ShareErrors.NotShareable;
        }

        foreach (var share in current.Where(s => target.All(w => w.PersonId != s.AccountId)))
        {
            share.SoftDelete(now);
        }

        foreach (var w in target)
        {
            if (current.FirstOrDefault(s => s.AccountId == w.PersonId) is { } existing)
            {
                if (existing.UpdateShare(expense, w.Amount) is { IsFailure: true } failed)
                {
                    return failed.Error;
                }

                continue;
            }

            var created = Transaction.CreateShare(expense, w.PersonId, w.Amount);
            if (created.IsFailure)
            {
                return created.Error;
            }

            db.Transactions.Add(created.Value);
        }

        return null;
    }
}
