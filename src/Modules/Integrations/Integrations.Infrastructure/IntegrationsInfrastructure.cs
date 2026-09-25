using System.Text.Json;
using Integrations.Application;
using Integrations.Application.Contracts;
using Integrations.Application.Sync;
using Integrations.Infrastructure.Demo;
using Integrations.Infrastructure.Ibkr;
using Integrations.Infrastructure.Persistence;
using Integrations.Infrastructure.Trading212;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Http;

namespace Integrations.Infrastructure;

internal sealed class DataProtectionCredentialProtector(IDataProtectionProvider provider) : ICredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("integrations.broker-credentials.v1");

    public string Protect(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(JsonSerializer.Serialize(credentials));

    public IReadOnlyDictionary<string, string> Unprotect(string protectedCredentials) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(protectedCredentials)) ?? [];
}

internal sealed class InvestmentProviderFactory(IServiceProvider services, IConfiguration config, TimeProvider clock)
    : IInvestmentProviderFactory
{
    public IReadOnlySet<BrokerKind> Available { get; } = config.GetValue<bool>("Integrations:EnableDemo")
        ? new HashSet<BrokerKind> { BrokerKind.Trading212, BrokerKind.InteractiveBrokers, BrokerKind.Demo }
        : new HashSet<BrokerKind> { BrokerKind.Trading212, BrokerKind.InteractiveBrokers };

    public IInvestmentProvider Create(BrokerKind kind, IReadOnlyDictionary<string, string> credentials)
    {
        if (!Available.Contains(kind))
        {
            throw new ProviderConfigurationException($"{kind} is not enabled on this server.");
        }

        string Required(string key) => credentials.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ProviderConfigurationException($"Missing credential '{key}'.");

        return kind switch
        {
            BrokerKind.Trading212 => new Trading212Provider(services.GetRequiredService<Trading212Client>()
                .Configure(Required("apiKey"), Required("apiSecret"), credentials.GetValueOrDefault("environment") == "demo")),
            BrokerKind.InteractiveBrokers => new IbkrFlexProvider(services.GetRequiredService<FlexClient>(),
                Required("token"), Required("queryId"), config.GetValue("Integrations:Ibkr:BackfillYears", 5), clock),
            _ => new DemoProvider(credentials.GetValueOrDefault("profile") == "b" ? "b" : "a", clock),
        };
    }
}

public static class IntegrationsInfrastructure
{
    public static IServiceCollection AddIntegrationsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IntegrationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations", IntegrationsDbContext.Schema)
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IIntegrationsDb>(sp => sp.GetRequiredService<IntegrationsDbContext>());
        services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
        services.AddSingleton<RateGate>();
        services.AddScoped<IInvestmentProviderFactory, InvestmentProviderFactory>();
        services.AddSingleton<ICsvHistoryParser, Trading212CsvHistoryParser>();

        // Broker HTTP clients: allow-listed to read endpoints only; no automatic retries of non-idempotent calls.
        services.AddHttpClient<Trading212Client>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(60);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("personal-finance/1.0");
            })
            .AddHttpMessageHandler(() => new AllowListHttpHandler(
                Trading212Client.AllowList(Trading212Client.LiveHost).Concat(Trading212Client.AllowList(Trading212Client.DemoHost))));
        services.AddHttpClient<FlexClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("personal-finance/1.0");
            })
            .AddHttpMessageHandler(() => new AllowListHttpHandler(FlexClient.AllowList()));
        return services;
    }
}
