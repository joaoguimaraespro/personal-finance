using Finance.Application.Abstractions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure;

public static class FinanceInfrastructure
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<FinanceDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations", FinanceDbContext.Schema)
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
        services.AddScoped<IFinanceDb>(sp => sp.GetRequiredService<FinanceDbContext>());
        return services;
    }
}
