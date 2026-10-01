using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FluentValidation;
using Finance.Application.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Host.Api.Auth;

public sealed record SetupRequest(string Email, string Password, string SetupToken);

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record MfaLoginRequest(string? Code, string? RecoveryCode, bool RememberMe);

public sealed record MfaEnableRequest(string Code);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record LoginResponse(string Status);

public sealed record MeResponse(bool Authenticated, bool SetupRequired, string? Email, bool MfaEnabled, bool MfaSatisfied);

/// <summary>When the current session ends if nothing renews it, so the UI can warn before it does.</summary>
public sealed record SessionResponse(DateTimeOffset ExpiresAtUtc, int IdleTimeoutMinutes, bool Persistent);

public sealed record MfaSetupResponse(string SharedKey, string OtpAuthUri);

public sealed class SetupValidator : AbstractValidator<SetupRequest>
{
    public SetupValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(256);
        RuleFor(x => x.SetupToken).NotEmpty();
    }
}

public static class AuthEndpoints
{
    public const string MfaPolicy = "mfa";

    /// <summary>Relative to /api/auth. Requests to it must not slide the session (Program.cs).</summary>
    public const string SessionPath = "/session";
    public const string LoginRateLimit = "login";
    private const string Issuer = "Personal Finance";

    private static readonly Error InvalidCredentials =
        Error.Failure("Auth.InvalidCredentials", "Invalid email or password.") with { Type = ErrorType.Unauthorized };

    private static readonly Error InvalidCode =
        Error.Failure("Auth.InvalidCode", "Invalid verification code.") with { Type = ErrorType.Unauthorized };

    public static void MapAuth(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapGet("/antiforgery", (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false, // Angular's HttpClient reads it and echoes it in X-XSRF-TOKEN.
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext ctx, UserManager<AppUser> users, AuthDbContext db, CancellationToken ct) =>
        {
            var setupRequired = !await db.Users.AnyAsync(ct);
            var user = await users.GetUserAsync(ctx.User);
            if (user is null)
            {
                return Results.Ok(new MeResponse(false, setupRequired, null, false, false));
            }

            return Results.Ok(new MeResponse(true, false, user.Email, await users.GetTwoFactorEnabledAsync(user),
                ctx.User.HasClaim("amr", "mfa")));
        });

        // First-run bootstrap: only works while no user exists and requires the out-of-band setup token.
        group.MapPost("/setup", async (SetupRequest req, UserManager<AppUser> users, AuthDbContext db,
            IConfiguration config, TimeProvider clock, CancellationToken ct) =>
        {
            var expected = config["Auth:SetupToken"];
            if (string.IsNullOrEmpty(expected) || expected.Length < 24 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(req.SetupToken),
                    Encoding.UTF8.GetBytes(expected)))
            {
                return ResultHttp.Problem(Error.Forbidden("Auth.Setup", "Invalid setup token."));
            }

            if (await db.Users.AnyAsync(ct))
            {
                return ResultHttp.Problem(Error.Conflict("Auth.Setup", "The owner account already exists."));
            }

            var user = new AppUser { UserName = req.Email, Email = req.Email, CreatedAtUtc = clock.GetUtcNow() };
            var result = await users.CreateAsync(user, req.Password);
            return result.Succeeded
                ? Results.NoContent()
                : Results.ValidationProblem(result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
        }).Validate<SetupRequest>().RequireRateLimiting(LoginRateLimit);

        group.MapPost("/login", async (LoginRequest req, SignInManager<AppUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(req.Email, req.Password, req.RememberMe,
                lockoutOnFailure: true);
            if (result.RequiresTwoFactor)
            {
                return Results.Ok(new LoginResponse("mfa_required"));
            }

            if (result.IsLockedOut)
            {
                return Results.Problem(title: "Auth.LockedOut", detail: "Too many attempts. Try again later.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            // Password-only session: can only reach the MFA enrollment endpoints.
            return result.Succeeded ? Results.Ok(new LoginResponse("mfa_enrollment_required"))
                : ResultHttp.Problem(InvalidCredentials);
        }).RequireRateLimiting(LoginRateLimit);

        group.MapPost("/login/mfa", async (MfaLoginRequest req, SignInManager<AppUser> signIn) =>
        {
            SignInResult result;
            if (!string.IsNullOrWhiteSpace(req.RecoveryCode))
            {
                result = await signIn.TwoFactorRecoveryCodeSignInAsync(req.RecoveryCode.Replace(" ", "",
                    StringComparison.Ordinal));
            }
            else
            {
                var code = (req.Code ?? "").Replace(" ", "", StringComparison.Ordinal);
                result = await signIn.TwoFactorAuthenticatorSignInAsync(code, req.RememberMe, rememberClient: false);
            }

            if (result.IsLockedOut)
            {
                return Results.Problem(title: "Auth.LockedOut", statusCode: StatusCodes.Status429TooManyRequests);
            }

            return result.Succeeded ? Results.Ok(new LoginResponse("ok")) : ResultHttp.Problem(InvalidCode);
        }).RequireRateLimiting(LoginRateLimit);

        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        // Session status for the UI. Reading it never extends the session (see OnCheckSlidingExpiration), so an idle
        // tab that polls it still times out; only real use or an explicit extend keeps the session alive.
        group.MapGet(SessionPath, async (HttpContext ctx, IOptionsMonitor<CookieAuthenticationOptions> cookies,
            TimeProvider clock) =>
        {
            var auth = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            var idle = cookies.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;
            return Results.Ok(new SessionResponse(auth.Properties?.ExpiresUtc ?? clock.GetUtcNow().Add(idle),
                (int)idle.TotalMinutes, auth.Properties?.IsPersistent ?? false));
        }).RequireAuthorization(MfaPolicy);

        // "Stay signed in": re-issue the same ticket (same claims, including amr=mfa) with a fresh idle timeout.
        group.MapPost(SessionPath + "/extend", async (HttpContext ctx,
            IOptionsMonitor<CookieAuthenticationOptions> cookies, TimeProvider clock) =>
        {
            var auth = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (!auth.Succeeded || auth.Principal is null)
            {
                return Results.Unauthorized();
            }

            var idle = cookies.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;
            var properties = auth.Properties ?? new AuthenticationProperties();
            var now = clock.GetUtcNow();
            properties.IssuedUtc = now;
            properties.ExpiresUtc = now.Add(idle);
            await ctx.SignInAsync(IdentityConstants.ApplicationScheme, auth.Principal, properties);
            return Results.Ok(new SessionResponse(properties.ExpiresUtc.Value, (int)idle.TotalMinutes,
                properties.IsPersistent));
        }).RequireAuthorization(MfaPolicy);

        var mfa = group.MapGroup("/mfa").RequireAuthorization();

        mfa.MapGet("/setup", async (HttpContext ctx, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(ctx.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (await users.GetTwoFactorEnabledAsync(user))
            {
                return ResultHttp.Problem(Error.Conflict("Auth.Mfa", "MFA is already enabled."));
            }

            // A fresh secret on every visit until MFA is enabled: a key that was seen, copied or photographed
            // during an abandoned enrolment is never the one that ends up protecting the account.
            await users.ResetAuthenticatorKeyAsync(user);
            var key = await users.GetAuthenticatorKeyAsync(user);

            var label = UrlEncoder.Default.Encode($"{Issuer}:{user.Email}");
            var uri = string.Create(CultureInfo.InvariantCulture,
                $"otpauth://totp/{label}?secret={key}&issuer={UrlEncoder.Default.Encode(Issuer)}&digits=6");
            return Results.Ok(new MfaSetupResponse(FormatKey(key!), uri));
        });

        mfa.MapPost("/enable", async (MfaEnableRequest req, HttpContext ctx, UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            var user = await users.GetUserAsync(ctx.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var code = req.Code.Replace(" ", "", StringComparison.Ordinal);
            if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            {
                return ResultHttp.Problem(InvalidCode);
            }

            await users.SetTwoFactorEnabledAsync(user, true);
            var recovery = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            // Upgrade the current session to a full MFA session.
            await signIn.SignInWithClaimsAsync(user, isPersistent: false, [new Claim("amr", "mfa")]);
            return Results.Ok(new { RecoveryCodes = recovery });
        }).RequireRateLimiting(LoginRateLimit);

        group.MapPost("/password", async (ChangePasswordRequest req, HttpContext ctx, UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            var user = await users.GetUserAsync(ctx.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            await signIn.RefreshSignInAsync(user);
            return Results.NoContent();
        }).RequireAuthorization(MfaPolicy).RequireRateLimiting(LoginRateLimit);
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return sb.ToString().TrimEnd().ToLowerInvariant();
    }
}
