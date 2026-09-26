using Ai.Application;
using Ai.Application.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ai.Infrastructure;

public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options), IAiDb
{
    public const string Schema = "ai";

    public DbSet<AiClient> Clients => Set<AiClient>();
    public DbSet<AiAuditEvent> AuditEvents => Set<AiAuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AiClient>(e =>
        {
            e.HasIndex(x => x.TokenPrefix).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.TokenPrefix).HasMaxLength(16);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Scopes).HasColumnType("text[]");
        });
        modelBuilder.Entity<AiAuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.Property(x => x.Arguments).HasColumnType("jsonb");
            e.Property(x => x.Tool).HasMaxLength(64);
            e.Property(x => x.Scope).HasMaxLength(64);
            e.Property(x => x.ClientName).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.ClientId);
        });
    }
}

public static class AiInfrastructure
{
    public static IServiceCollection AddAiInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AiDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", AiDbContext.Schema))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAiDb>(sp => sp.GetRequiredService<AiDbContext>());
        return services;
    }
}
