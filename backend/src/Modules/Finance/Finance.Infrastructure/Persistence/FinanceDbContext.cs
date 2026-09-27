using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Goals;
using Finance.Domain.Imports;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, IDataProtectionProvider protection)
    : DbContext(options), IFinanceDb
{
    public const string Schema = "finance";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AllocationBucket> Buckets => Set<AllocationBucket>();
    public DbSet<AllocationCheck> AllocationChecks => Set<AllocationCheck>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<ExpectedTransaction> ExpectedTransactions => Set<ExpectedTransaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<FinancialGoal> Goals => Set<FinancialGoal>();
    public DbSet<ImportBatch> Imports => Set<ImportBatch>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is numeric(19,4) everywhere; never float.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema(Schema);

        // Account identifiers (IBAN, account numbers) are encrypted at rest with ASP.NET Data Protection.
        var protector = protection.CreateProtector("finance.account-identifier.v1");
        var identifierConverter = new ValueConverter<string?, string?>(
            v => v == null ? null : protector.Protect(v),
            v => v == null ? null : protector.Unprotect(v));

        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Institution).HasMaxLength(80);
            e.Property(x => x.Identifier).HasConversion(identifierConverter).HasMaxLength(1024);
            e.Ignore(x => x.IsManual);
            e.Ignore(x => x.IsLiability);
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Color).HasMaxLength(7);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AllocationBucket>(e =>
        {
            e.ToTable("allocation_buckets");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(60);
        });

        b.Entity<AllocationCheck>(e =>
        {
            e.HasKey(x => new { x.Year, x.Month, x.BucketId });
            e.Ignore(x => x.Period);
            e.HasOne<AllocationBucket>().WithMany().HasForeignKey(x => x.BucketId);
        });

        b.Entity<Transaction>(e =>
        {
            e.HasQueryFilter(x => x.DeletedAtUtc == null);
            e.Property(x => x.OriginalCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.FxRate).HasPrecision(19, 10);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.Property(x => x.AssetSymbol).HasMaxLength(32);
            e.Property(x => x.AssetIsin).HasMaxLength(12);
            e.Property(x => x.AssetPriceSource).HasMaxLength(32);
            e.Property(x => x.AssetQuantity).HasPrecision(28, 10);
            e.Property(x => x.AssetUnitPrice).HasPrecision(28, 10);
            e.HasIndex(x => x.OccurredOn);
            e.HasIndex(x => new { x.Type, x.OccurredOn });
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.ImportId);
            // Idempotency for imports and broker syncs: the same external record can only land once.
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.CounterAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AllocationBucket>().WithMany().HasForeignKey(x => x.BucketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FinancialGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TransactionAudit>(e =>
        {
            e.ToTable("transaction_audit");
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.Changes).HasColumnType("jsonb");
            e.HasIndex(x => x.TransactionId);
        });

        b.Entity<RecurringTransaction>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ExpectedTransaction>(e =>
        {
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => new { x.RecurringTransactionId, x.DueOn }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne<RecurringTransaction>().WithMany().HasForeignKey(x => x.RecurringTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Budget>(e =>
        {
            e.HasIndex(x => x.EffectiveFrom).IsUnique();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<BudgetItem>(e =>
        {
            e.Property(x => x.Value).HasPrecision(19, 6);
            e.HasOne<AllocationBucket>().WithMany().HasForeignKey(x => x.BucketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.FileSha256).HasMaxLength(64);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.CreatedAtUtc);
        });

        b.Entity<FinancialGoal>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Icon).HasMaxLength(40);
        });
    }
}
