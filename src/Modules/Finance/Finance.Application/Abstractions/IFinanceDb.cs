using Finance.Domain.Accounts;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Goals;
using Finance.Domain.Imports;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    /// <summary>Stable actor label for audit trails (user name, "system:recurring", "import:{id}").</summary>
    string Actor { get; }
}
