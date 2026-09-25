using Integrations.Application;
using Integrations.Application.Connections;
using Microsoft.EntityFrameworkCore;

namespace Integrations.Infrastructure.Persistence;

public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options), IIntegrationsDb
{
    public const string Schema = "integrations";

    public DbSet<BrokerConnection> Connections => Set<BrokerConnection>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<BrokerConnection>(e =>
        {
            e.ToTable("broker_connections");
            e.Property(x => x.ProtectedCredentials).HasMaxLength(4000);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.AccountId).IsUnique();
        });
        modelBuilder.Entity<SyncJob>(e =>
        {
            e.Property(x => x.Errors).HasColumnType("jsonb").HasMaxLength(20000);
            e.HasIndex(x => new { x.ConnectionId, x.StartedAtUtc });
            e.HasOne<BrokerConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
