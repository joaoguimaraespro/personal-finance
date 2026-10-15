using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Integration.Tests;

/// <summary>The API as the desktop app runs it (own database in production; a test container here).</summary>
public sealed class DesktopApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17.6-alpine")
        .WithDatabase("finance").WithUsername("finance_test").WithPassword("finance_test").Build();

    public async ValueTask InitializeAsync() => await _db.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Finance", _db.GetConnectionString());
        builder.UseSetting("Desktop:Enabled", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

    public ApiClient NewClient() => new(CreateDefaultClient(new CookieAndCsrfHandler()));
}

/// <summary>On the desktop app the owner signs up without a setup token and two-factor is optional.</summary>
public sealed class DesktopModeTests(DesktopApiFactory factory) : IClassFixture<DesktopApiFactory>
{
    [Fact]
    public async Task The_owner_signs_up_without_a_token_and_gets_in_with_the_password_alone()
    {
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();
        var me = await client.GetJsonAsync("/api/auth/me");
        me.GetProperty("desktop").GetBoolean().ShouldBeTrue();
        me.GetProperty("setupRequired").GetBoolean().ShouldBeTrue();

        await ApiClient.EnsureAsync(await client.PostAsync("/api/auth/setup",
            new { email = "me@example.test", password = "a long desktop passphrase" }));
        var login = await client.PostAsync("/api/auth/login",
            new { email = "me@example.test", password = "a long desktop passphrase", rememberMe = false });
        (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("ok");
        await client.RefreshCsrfAsync();

        (await client.GetJsonAsync("/api/auth/me")).GetProperty("mfaSatisfied").GetBoolean().ShouldBeTrue();
        (await client.Http.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // A second owner can never be created, token or not.
        (await client.PostAsync("/api/auth/setup", new { email = "x@example.test", password = "another long passphrase" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
