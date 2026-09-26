using Investments.Application.Abstractions;
using Investments.Infrastructure.Fx;
using Investments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Http;

namespace Investments.Infrastructure;

public static class InvestmentsInfrastructure
{
    public static IServiceCollection AddInvestmentsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InvestmentsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations", InvestmentsDbContext.Schema)
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IInvestmentsDb>(sp => sp.GetRequiredService<InvestmentsDbContext>());

        services.AddHttpClient<IFxRateSource, EcbFxRateSource>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("personal-finance/1.0");
            })
            .AddHttpMessageHandler(() => new AllowListHttpHandler(
                [AllowedRequest.Get(EcbFxRateSource.Host, "/stats/eurofxref/eurofxref-(daily|hist-90d|hist)\\.xml")]))
            .AddStandardResilienceHandler();
        return services;
    }
}
