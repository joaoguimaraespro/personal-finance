using Finance.Application.Abstractions;
using Finance.Domain.Allocation;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Transactions;

/// <summary>Cross-aggregate checks a transaction draft must pass before it touches the ledger.</summary>
public static class TransactionReferences
{
    public static async Task<Result<TransactionDraft>> ResolveAsync(IFinanceDb db, TransactionDraft draft,
        CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.AccountId, ct);
        if (account is null || account.ArchivedAtUtc is not null)
        {
            return Error.Validation("Transaction.Account", "Account does not exist or is archived.");
        }

        if (!account.IsManual)
        {
            return Error.Validation("Transaction.Account", "Broker accounts are read-only.");
        }

        if (account.Currency != Currency.Base && draft.Currency != account.Currency)
        {
            return Error.Validation("Transaction.Currency",
                $"Entries in a {account.Currency} account must be in {account.Currency}.");
        }

        if (draft.CounterAccountId is { } counterId)
        {
            var counter = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == counterId, ct);
            if (counter is null || counter.ArchivedAtUtc is not null)
            {
                return Error.Validation("Transaction.CounterAccount", "Destination account does not exist.");
            }

            if (counter.Currency != Currency.Base && counter.Currency != draft.Currency)
            {
                return Error.Validation("Transaction.CounterAccount",
                    "Cross-currency transfers to non-EUR accounts are not supported yet.");
            }
        }

        var nature = draft.Nature;
        if (draft.Type is TransactionType.Expense or TransactionType.Income && draft.CategoryId is { } categoryId)
        {
            var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            var expected = draft.Type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
            if (category is null || category.Type != expected)
            {
                return Error.Validation("Transaction.Category", $"Category must be an {expected} category.");
            }

            nature ??= category.DefaultNature;
        }

        var splits = draft.Splits;
        if (draft.Type is TransactionType.Expense or TransactionType.Income && draft.Splits is { Count: > 0 } lines)
        {
            // Every line must be a category of the transaction's own type. Each expense line takes the transaction's
            // explicit nature, or else its category's default nature (Variable when the category has none).
            var expected = draft.Type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
            var ids = lines.Select(l => l.CategoryId).Distinct().ToList();
            var categories = await db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);
            if (ids.Any(id => !categories.TryGetValue(id, out var c) || c.Type != expected))
            {
                return Error.Validation("Transaction.Splits", $"Every split line needs an {expected} category.");
            }

            splits = lines.Select(l => l with
            {
                Nature = draft.Type == TransactionType.Expense
                    ? draft.Nature ?? categories[l.CategoryId].DefaultNature ?? ExpenseNature.Variable
                    : null,
            }).ToList();
        }

        if ((draft.Type == TransactionType.Savings || TransactionTypes.IsInvestment(draft.Type)) &&
            draft.BucketId is { } bucketId)
        {
            var bucket = await db.Buckets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bucketId, ct);
            var expected = draft.Type == TransactionType.Savings ? BucketGroup.Savings : BucketGroup.Investment;
            if (bucket is null || bucket.Group != expected)
            {
                return Error.Validation("Transaction.Bucket", $"Bucket must be a {expected} bucket.");
            }
        }

        if (draft.GoalId is { } goalId && !await db.Goals.AnyAsync(g => g.Id == goalId, ct))
        {
            return Error.Validation("Transaction.Goal", "Goal does not exist.");
        }

        return draft with { Nature = nature, Splits = splits };
    }
}
