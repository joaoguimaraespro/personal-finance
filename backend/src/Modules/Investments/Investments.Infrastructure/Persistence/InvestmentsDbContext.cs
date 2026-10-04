using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Investments.Infrastructure.Persistence;

public sealed class InvestmentsDbContext(DbContextOptions<InvestmentsDbContext> options) : DbContext(options), IInvestmentsDb
{
    public const string Schema = "investments";

    public DbSet<Security> Securities => Set<Security>();
    public DbSet<BrokerSymbol> BrokerSymbols => Set<BrokerSymbol>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Dividend> Dividends => Set<Dividend>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashBalance> CashBalances => Set<CashBalance>();
    public DbSet<MarketPrice> MarketPrices => Set<MarketPrice>();
    public DbSet<PriceListing> PriceListings => Set<PriceListing>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
    public DbSet<TargetAllocation> TargetAllocations => Set<TargetAllocation>();
    public DbSet<ManualAsset> ManualAssets => Set<ManualAsset>();
    public DbSet<NetWorthSnapshot> NetWorthSnapshots => Set<NetWorthSnapshot>();
    public DbSet<ManualHolding> ManualHoldings => Set<ManualHolding>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema(Schema);

        b.Entity<Security>(e =>
        {
            e.HasIndex(x => x.Isin).IsUnique().HasFilter("isin IS NOT NULL");
            e.Property(x => x.Isin).HasMaxLength(12);
            e.Property(x => x.Symbol).HasMaxLength(40);
            e.Property(x => x.Exchange).HasMaxLength(40);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Ignore(x => x.EffectiveAssetClass);
        });

        b.Entity<BrokerSymbol>(e =>
        {
            e.HasKey(x => new { x.Source, x.Symbol });
            e.Property(x => x.Symbol).HasMaxLength(64);
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        });

        b.Entity<Position>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.SecurityId });
            // Fractional shares: quantities need more precision than money.
            e.Property(x => x.Quantity).HasPrecision(28, 10);
            // Coins can be worth fractions of a cent: prices keep 12 decimals.
            e.Property(x => x.AveragePrice).HasPrecision(28, 12);
            e.Property(x => x.LastPrice).HasPrecision(28, 12);
            e.Ignore(x => x.CostBasis);
            e.Ignore(x => x.MarketValue);
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        });

        b.Entity<Trade>(e =>
        {
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.ExecutedAtUtc });
            e.Property(x => x.Quantity).HasPrecision(28, 10);
            e.Property(x => x.Price).HasPrecision(28, 12);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        });

        b.Entity<Dividend>(e =>
        {
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.PaidOn);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        });

        b.Entity<CashMovement>(e =>
        {
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.OccurredAtUtc });
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.Ignore(x => x.IsExternalFlow);
        });

        b.Entity<CashBalance>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.Currency });
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        });

        b.Entity<MarketPrice>(e =>
        {
            e.HasKey(x => new { x.SecurityId, x.Date });
            e.Property(x => x.Close).HasPrecision(28, 12);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        });

        b.Entity<PriceListing>(e =>
        {
            e.HasKey(x => x.SecurityId);
            e.Property(x => x.Provider).HasMaxLength(16);
            e.Property(x => x.Symbol).HasMaxLength(40);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FxRate>(e =>
        {
            e.HasKey(x => new { x.Date, x.Quote });
            e.Property(x => x.Quote).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Rate).HasPrecision(19, 10);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Ignore(x => x.ToEurFactor);
        });

        b.Entity<PortfolioSnapshot>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.Date });
            e.Property(x => x.Origin).HasMaxLength(16);
            e.Ignore(x => x.TotalBase);
        });

        b.Entity<TargetAllocation>(e =>
        {
            e.HasKey(x => x.AssetClass);
            e.Property(x => x.Percent).HasPrecision(9, 6);
        });

        b.Entity<ManualAsset>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Ignore(x => x.IsLiability);
            e.HasMany(x => x.Valuations).WithOne().HasForeignKey(v => v.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Valuations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<AssetValuation>(e => e.HasKey(x => new { x.AssetId, x.Date }));

        b.Entity<NetWorthSnapshot>(e =>
        {
            e.HasKey(x => x.Date);
            e.Property(x => x.Breakdown).HasColumnType("jsonb").HasMaxLength(4000);
            e.Ignore(x => x.NetWorthBase);
        });

        b.Entity<ManualHolding>(e =>
        {
            e.HasIndex(x => new { x.AccountId, x.SecurityId }).IsUnique();
            e.Property(x => x.Quantity).HasPrecision(28, 10);
            e.Property(x => x.AveragePrice).HasPrecision(28, 12);
            e.Property(x => x.Notes).HasMaxLength(ManualHolding.MaxNotes);
            e.Ignore(x => x.RewardQuantity);
            e.Ignore(x => x.TotalQuantity);
            e.Ignore(x => x.Cost);
            e.Ignore(x => x.AverageCostIncludingRewards);
            e.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
            e.HasMany(x => x.Rewards).WithOne().HasForeignKey(r => r.HoldingId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Rewards).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<HoldingReward>(e =>
        {
            e.ToTable("holding_rewards");
            // Ids are assigned in the domain: a reward added to a loaded holding is new, not a stale update.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Quantity).HasPrecision(28, 10);
            e.Property(x => x.Note).HasMaxLength(ManualHolding.MaxNotes);
        });
    }
}
