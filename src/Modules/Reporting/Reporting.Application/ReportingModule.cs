using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Queries;

namespace Reporting.Application;

public static class ReportingModule
{
    public static IServiceCollection AddReporting(this IServiceCollection services)
    {
        services.AddScoped<LedgerAggregates>();
        return services;
    }
}
