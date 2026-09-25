using Finance.Infrastructure.Persistence;
using Host.Api.Auth;
using Investments.Infrastructure.Persistence;
using Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Infrastructure;

internal static class Database
{
    /// <summary>Applies migrations for every module and seeds reference data. Run by the one-shot migrate job.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await auth.Database.MigrateAsync(ct);
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        await finance.Database.MigrateAsync(ct);
        await FinanceSeeder.SeedAsync(finance, ct);
        await scope.ServiceProvider.GetRequiredService<InvestmentsDbContext>().Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Database.MigrateAsync(ct);
    }
}
