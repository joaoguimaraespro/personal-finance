using Investments.Application.Abstractions;
using Investments.Infrastructure.Fx;
using Investments.Infrastructure.MarketData;
using Investments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SharedKernel.Http;

namespace Investments.Infrastructure;

public static class InvestmentsInfrastructure
{
    public static IServiceCollection AddInvestmentsInfrastructure(this IServiceCollection services, string connectionString,
        IConfiguration configuration)
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

        // Public closing prices for reconstructing history (MarketData:Provider = yahoo | none).
        var provider = configuration["MarketData:Provider"]?.Trim().ToLowerInvariant() ?? "yahoo";
        if (provider == "yahoo")
        {
            services.AddHttpClient<IPriceHistorySource, YahooPriceHistorySource>(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(60);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("personal-finance/1.0 (self-hosted)");
                    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                })
                .AddHttpMessageHandler(() => new AllowListHttpHandler(YahooPriceHistorySource.AllowList))
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts = 2;
                    o.Retry.Delay = TimeSpan.FromSeconds(2);
                    o.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
                    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(50);
                });
        }
        else
        {
            services.AddSingleton<IPriceHistorySource, DisabledPriceHistorySource>();
        }

        return services;
    }
}
