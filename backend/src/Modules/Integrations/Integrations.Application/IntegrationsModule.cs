using FluentValidation;
using Integrations.Application.Instruments;
using Integrations.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Integrations.Application;

public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateConnectionValidator>();
        services.AddSingleton<SyncQueue>();
        services.AddScoped<SyncPipeline>();
        services.AddScoped<PortfolioInstrumentSource>();
        return services;
    }
}
