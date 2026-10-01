using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Integration.Tests.Infrastructure;

/// <summary>Runs the real API against a throwaway PostgreSQL container.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SetupToken = "integration-setup-token-0123456789";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17.6-alpine")
        .WithDatabase("finance")
        .WithUsername("finance_test")
        .WithPassword("finance_test")
        .Build();

    private OwnerSession? _owner;

    /// <summary>Replaces Claude in tests; scripted per test.</summary>
    public ScriptedAssistantModel Assistant { get; } = new();

    /// <summary>Replaces the public price provider: no test ever calls Yahoo.</summary>
    public FakePriceHistory Prices { get; } = new();

    public async ValueTask InitializeAsync() => await _db.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Finance", _db.GetConnectionString());
        builder.UseSetting("Auth:SetupToken", SetupToken);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimits:LoginPerMinute", "1000");
        builder.UseSetting("Integrations:EnableDemo", "true");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Ai.Application.Assistant.IAssistantModel>(Assistant);
            services.AddSingleton<Investments.Application.Abstractions.IPriceHistorySource>(Prices);
        });
    }

    public ApiClient NewClient() => new(CreateDefaultClient(new CookieAndCsrfHandler()));

    /// <summary>A client logged in as the owner with MFA completed. Created once per test run.</summary>
    public async Task<ApiClient> OwnerAsync()
    {
        _owner ??= await OwnerSession.CreateAsync(this);
        return await _owner.SignInAsync(this);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
