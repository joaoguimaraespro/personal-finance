using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Finance.Application;
using Finance.Application.Abstractions;
using Finance.Infrastructure;
using Host.Api.Auth;
using Host.Api.Infrastructure;
using Host.Api.Jobs;
using Imports.Application;
using Investments.Application;
using Investments.Infrastructure;
using Integrations.Application;
using Integrations.Infrastructure;
using Exports.Application;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reporting.Application;
using Scalar.AspNetCore;

// Container health probe for the chiseled image (no shell or curl inside).
if (args.Contains("--healthcheck"))
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
    try
    {
        var response = await probe.GetAsync($"http://127.0.0.1:{port}/health/live");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var connectionString = config.GetConnectionString("Finance")
                       ?? throw new InvalidOperationException("ConnectionStrings:Finance is required.");

builder.AddObservability();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Data Protection keys encrypt auth cookies and account identifiers; they live on a dedicated, backed-up volume.
var keyPath = config["DataProtection:KeyPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("personal-finance");
if (!string.IsNullOrWhiteSpace(keyPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

// ---- Modules
builder.Services.AddFinanceInfrastructure(connectionString);
builder.Services.AddFinanceApplication();
builder.Services.AddReporting();
builder.Services.AddImports();
builder.Services.AddInvestmentsInfrastructure(connectionString);
builder.Services.AddInvestmentsApplication();
builder.Services.AddIntegrationsInfrastructure(connectionString);
builder.Services.AddIntegrationsApplication();
builder.Services.AddExports();

// ---- Identity (single owner, mandatory TOTP MFA)
builder.Services.AddDbContext<AuthDbContext>(o => o
    .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", AuthDbContext.Schema))
    .UseSnakeCaseNamingConvention());
builder.Services
    .AddIdentity<AppUser, IdentityRole<Guid>>(o =>
    {
        // Length over composition rules (NIST SP 800-63B).
        o.Password.RequiredLength = 12;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireDigit = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// Plain-HTTP local development and tests can't carry Secure cookies; everywhere else TLS is mandatory.
var secureCookies = !builder.Environment.IsEnvironment("Testing") && !builder.Environment.IsDevelopment();

// TLS terminates at the reverse proxy. Only trust X-Forwarded-* from the proxy's network, never from clients.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var cidr in config.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = secureCookies ? "__Host-pf-session" : "pf-session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    // An API never redirects to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
{
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthEndpoints.MfaPolicy, p => p.RequireAuthenticatedUser().RequireClaim("amr", "mfa"));
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = secureCookies ? "__Host-pf-af" : "pf-af";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

// ---- Rate limiting
var loginPermits = config.GetValue("RateLimits:LoginPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermits, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);
builder.Services.AddHostedService<RecurringProposalJob>();
builder.Services.AddHostedService<DailySnapshotJob>();
builder.Services.AddHostedService<BrokerSyncService>();
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

var app = builder.Build();

if (args.Contains("--migrate"))
{
    await Database.MigrateAsync(app.Services);
    return 0;
}

if (app.Environment.IsDevelopment() || config.GetValue<bool>("Database:MigrateOnStartup"))
{
    await Database.MigrateAsync(app.Services);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AntiforgeryMiddleware>();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var api = app.MapGroup("/api");
api.MapAuth();

var owner = api.MapGroup("").RequireAuthorization(AuthEndpoints.MfaPolicy);
owner.MapFinanceEndpoints();
owner.MapReports();
owner.MapImports();
owner.MapInvestments();
owner.MapIntegrations();
owner.MapExports();

await app.RunAsync();
return 0;

public partial class Program;
