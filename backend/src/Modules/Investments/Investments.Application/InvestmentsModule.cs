using FluentValidation;
using Investments.Application.Fx;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;
using Investments.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Investments.Application;

public static class InvestmentsModule
{
    public static IServiceCollection AddInvestmentsApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<ManualAssetValidator>();
        services.AddScoped<FxRates>();
        services.AddScoped<PortfolioQueries>();
        services.AddScoped<PortfolioSyncWriter>();
        services.AddScoped<PortfolioSnapshotter>();
        services.AddScoped<NetWorthService>();
        return services;
    }
}
