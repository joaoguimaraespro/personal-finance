using Finance.Domain.Accounts;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Goals;
using Finance.Domain.Imports;
using Finance.Domain.Interest;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Application.Abstractions;

/// <summary>Unit of work over the finance schema. Soft-deleted transactions are filtered out by default.</summary>
public interface IFinanceDb
{
    DbSet<Account> Accounts { get; }
    DbSet<Category> Categories { get; }
    DbSet<AllocationBucket> Buckets { get; }
    DbSet<AllocationCheck> AllocationChecks { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<TransactionAudit> TransactionAudits { get; }
    DbSet<RecurringTransaction> RecurringTransactions { get; }
    DbSet<ExpectedTransaction> ExpectedTransactions { get; }
    DbSet<Budget> Budgets { get; }
    DbSet<FinancialGoal> Goals { get; }
    DbSet<ImportBatch> Imports { get; }
    DbSet<AccountInterestRate> InterestRates { get; }
    DbSet<InterestMonth> InterestMonths { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    /// <summary>Stable actor label for audit trails (user name, "system:recurring", "import:{id}").</summary>
    string Actor { get; }
}

/// <summary>
/// Per-request override of the audit actor. The AI gateway sets it (e.g. "ai:Claude Code") while a write tool runs,
/// so the ledger's audit trail shows that an AI client — not the signed-in owner or "system" — made the change.
/// </summary>
public sealed class ActorScope
{
    public string? Current { get; private set; }

    public IDisposable Use(string actor)
    {
        var previous = Current;
        Current = actor.Length > 100 ? actor[..100] : actor;
        return new Reset(() => Current = previous);
    }

    private sealed class Reset(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
