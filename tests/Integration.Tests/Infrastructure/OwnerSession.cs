using System.Net.Http.Json;
using System.Text.Json;

namespace Integration.Tests.Infrastructure;

/// <summary>Bootstraps the single owner account (setup token → password → TOTP enrolment) once per run.</summary>
public sealed class OwnerSession
{
    public const string Email = "owner@example.test";
    public const string Password = "correct-horse-battery-staple";

    private OwnerSession(string sharedKey) => SharedKey = sharedKey;

    public string SharedKey { get; }

    public static async Task<OwnerSession> CreateAsync(ApiFactory factory)
    {
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();
        await ApiClient.EnsureAsync(await client.PostAsync("/api/auth/setup",
            new { email = Email, password = Password, setupToken = ApiFactory.SetupToken }));
        await ApiClient.EnsureAsync(await client.PostAsync("/api/auth/login",
            new { email = Email, password = Password, rememberMe = false }));
        await client.RefreshCsrfAsync();

        var setup = await client.GetJsonAsync("/api/auth/mfa/setup");
        var key = setup.GetProperty("sharedKey").GetString()!;
        await ApiClient.EnsureAsync(await client.PostAsync("/api/auth/mfa/enable", new { code = Totp.Code(key) }));
        return new OwnerSession(key);
    }

    public async Task<ApiClient> SignInAsync(ApiFactory factory)
    {
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();
        var login = await client.PostAsync("/api/auth/login", new { email = Email, password = Password, rememberMe = false });
        await ApiClient.EnsureAsync(login);
        (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .ShouldBe("mfa_required");
        await ApiClient.EnsureAsync(await client.PostAsync("/api/auth/login/mfa", new { code = Totp.Code(SharedKey) }));
        await client.RefreshCsrfAsync();
        return client;
    }
}
