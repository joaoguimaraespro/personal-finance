using System.Net.Http.Json;
using System.Net;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Anonymous_requests_to_financial_data_are_rejected()
    {
        var client = factory.NewClient();

        (await client.Http.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.Http.GetAsync("/api/reports/overview/2026")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Setup_requires_the_out_of_band_token()
    {
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();

        var response = await client.PostAsync("/api/auth/setup",
            new { email = "intruder@example.test", password = "long-enough-password", setupToken = "guess-guess-guess-guess-guess" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Setup_cannot_create_a_second_owner()
    {
        await factory.OwnerAsync();
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();

        var response = await client.PostAsync("/api/auth/setup",
            new { email = "second@example.test", password = "long-enough-password", setupToken = ApiFactory.SetupToken });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Password_alone_never_reaches_financial_data()
    {
        await factory.OwnerAsync();
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();
        await client.PostAsync("/api/auth/login",
            new { email = OwnerSession.Email, password = OwnerSession.Password, rememberMe = false });

        // The session is only half-authenticated until the TOTP code is verified.
        (await client.Http.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var wrong = await client.PostAsync("/api/auth/login/mfa", new { code = "000000" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        await factory.OwnerAsync();
        var client = factory.NewClient();
        await client.RefreshCsrfAsync();

        var response = await client.PostAsync("/api/auth/login",
            new { email = OwnerSession.Email, password = "not-the-password", rememberMe = false });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mutations_without_the_csrf_header_are_rejected()
    {
        var client = await factory.OwnerAsync();
        var handlerless = factory.NewClient();
        // Same cookies, no X-XSRF-TOKEN header: simulates a cross-site form post.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts")
        {
            Content = JsonContent.Create(new { name = "Evil", kind = "Bank", currency = "EUR", openingBalance = 0 }),
        };
        foreach (var cookie in client.Http.DefaultRequestHeaders)
        {
            request.Headers.TryAddWithoutValidation(cookie.Key, cookie.Value);
        }

        var response = await handlerless.Http.SendAsync(request);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mfa_session_can_read_and_logout_ends_it()
    {
        var client = await factory.OwnerAsync();

        (await client.Http.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await client.GetJsonAsync("/api/auth/me");
        me.GetProperty("mfaSatisfied").GetBoolean().ShouldBeTrue();

        (await client.PostAsync("/api/auth/logout", new { })).EnsureSuccessStatusCode();
        (await client.Http.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
