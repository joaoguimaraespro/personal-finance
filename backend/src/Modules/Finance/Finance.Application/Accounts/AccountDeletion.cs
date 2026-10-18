using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Accounts;

/// <summary>What deleting an account takes with it, shown before the owner confirms.</summary>
/// <param name="SharedParts">People's shares of expenses paid from this account (they go with those expenses).</param>
public sealed record AccountDeletionPreview(Guid Id, string Name, AccountKind Kind, int Transactions, int Recurring,
    int SharedParts, bool HasLoanTerms);

/// <summary>
/// Deleting an account for good, unlike archiving it: its movements (and transfers to or from it), recurring items
/// paid from it, the people's shares of its expenses, its loan terms and interest history go with it. A person who
/// is deleted stops sharing recurring items. Broker accounts belong to their connection and are removed there.
/// </summary>
public static class AccountDeletion
{
    public static readonly Error BrokerAccount = Error.Validation("Account.Delete",
        "Broker accounts and crypto wallets are removed with their connection or wallet.");

    public static async Task<Result<AccountDeletionPreview>> PreviewAsync(IFinanceDb db, Guid id, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return AccountEndpoints.NotFound;
        }

        if (account.Kind == AccountKind.Broker)
        {
            return BrokerAccount;
        }

        var own = Own(db, id);
        return new AccountDeletionPreview(account.Id, account.Name, account.Kind, await own.CountAsync(ct),
            await db.RecurringTransactions.CountAsync(r => r.AccountId == id || r.CounterAccountId == id, ct),
            await db.Transactions.IgnoreQueryFilters()
                .CountAsync(t => t.SharedFromId != null && own.Select(o => o.Id).Contains(t.SharedFromId.Value) &&
                                 t.AccountId != id, ct),
            await db.Loans.AnyAsync(l => l.AccountId == id, ct));
    }

    public static async Task<Result<AccountDeletionPreview>> DeleteAsync(IFinanceDb db, Guid id, CancellationToken ct)
    {
        var preview = await PreviewAsync(db, id, ct);
        if (preview.IsFailure)
        {
            return preview;
        }

        // Its movements (deleted ones too, so nothing points at the account), then the shares of those expenses.
        var transactions = await Own(db, id).ToListAsync(ct);
        var ids = transactions.Select(t => t.Id).ToList();
        var shares = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.SharedFromId != null && ids.Contains(t.SharedFromId.Value) && !ids.Contains(t.Id))
            .ToListAsync(ct);
        var gone = ids.Concat(shares.Select(s => s.Id)).ToList();
        db.TransactionAudits.RemoveRange(await db.TransactionAudits.Where(a => gone.Contains(a.TransactionId))
            .ToListAsync(ct));
        db.Transactions.RemoveRange(transactions);
        db.Transactions.RemoveRange(shares);

        db.RecurringTransactions.RemoveRange(await db.RecurringTransactions
            .Where(r => r.AccountId == id || r.CounterAccountId == id).ToListAsync(ct));
        foreach (var shared in await db.RecurringTransactions.Where(r => r.Shares.Any(s => s.PersonId == id))
                     .ToListAsync(ct))
        {
            shared.RemoveShare(id);
        }

        // Loan terms and interest history are removed with the account by the database (cascade).
        db.Accounts.Remove(await db.Accounts.FirstAsync(a => a.Id == id, ct));
        await db.SaveChangesAsync(ct);
        return preview;
    }

    private static IQueryable<Domain.Transactions.Transaction> Own(IFinanceDb db, Guid id) =>
        db.Transactions.IgnoreQueryFilters().Where(t => t.AccountId == id || t.CounterAccountId == id);
}
