using Imports.Application.FinanceTracker;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Imports.Application;

public static class ImportsModule
{
    public static IServiceCollection AddImports(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder api) => ImportEndpoints.MapImports(api);
}
