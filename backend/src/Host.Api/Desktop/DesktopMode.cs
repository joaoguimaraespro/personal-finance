using Microsoft.Extensions.FileProviders;

namespace Host.Api.Desktop;

/// <summary>
/// Running as the desktop app (Windows/macOS installer) instead of on a server: one person on their own computer, the
/// API on 127.0.0.1 over plain HTTP, its own PostgreSQL, the web app served by the API itself (no reverse proxy).
/// Sign-in needs no setup token and two-factor authentication is optional (it can be turned on in Settings).
/// </summary>
public sealed record DesktopMode(bool Enabled)
{
    public static DesktopMode From(IConfiguration config) => new(config.GetValue<bool>("Desktop:Enabled"));
}

internal static class DesktopHosting
{
    /// <summary>The content security policy Caddy sets on a server, for the pages the API now serves itself.</summary>
    private const string PageCsp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
                                   "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; " +
                                   "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Starts the embedded PostgreSQL and points the app at it and at the user's data folder.</summary>
    public static async Task<EmbeddedPostgres> PrepareAsync(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var dataDir = config["Desktop:DataDir"] ?? throw new InvalidOperationException("Desktop:DataDir is required.");
        var binDir = config["Desktop:PostgresBin"] ??
                     throw new InvalidOperationException("Desktop:PostgresBin is required.");
        var pg = await EmbeddedPostgres.StartAsync(binDir, dataDir, CancellationToken.None);
        config["ConnectionStrings:Finance"] = pg.ConnectionString;
        // Keys must survive restarts (sessions, encrypted identifiers): appsettings.json leaves the path empty.
        if (string.IsNullOrWhiteSpace(config["DataProtection:KeyPath"]))
        {
            config["DataProtection:KeyPath"] = Path.Combine(dataDir, "keys");
        }
        config["Database:MigrateOnStartup"] = "true";
        return pg;
    }

    /// <summary>
    /// The launcher closes the app when its window closes: <c>POST /desktop/shutdown</c> with the token it generated
    /// at start (only the launcher knows it), which stops the API and, with it, PostgreSQL.
    /// </summary>
    public static void MapDesktopShutdown(this WebApplication app)
    {
        var token = app.Configuration["Desktop:ShutdownToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        app.MapPost("/desktop/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            var sent = ctx.Request.Headers["X-Desktop-Token"].ToString();
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(sent), System.Text.Encoding.UTF8.GetBytes(token)))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            lifetime.StopApplication();
            return Results.NoContent();
        });
    }

    /// <summary>The Angular build, with index.html for every client-side route; hashed assets cached for a year.</summary>
    public static void UseDesktopWebApp(this WebApplication app)
    {
        var root = app.Configuration["Desktop:WebRoot"];
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        var files = new PhysicalFileProvider(Path.GetFullPath(root));
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.Headers["Content-Security-Policy"] = PageCsp;
            }

            await next();
        });
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = c =>
            {
                var name = c.File.Name;
                c.Context.Response.Headers.CacheControl = name == "index.html" || name.EndsWith(".json", StringComparison.Ordinal)
                    ? "no-cache"
                    : "public, max-age=31536000, immutable";
            },
        });
        // Client-side routes (/dashboard, /accounts…): index.html, unless the API answers the path.
        app.Use(async (ctx, next) =>
        {
            if (ctx.GetEndpoint() is null && HttpMethods.IsGet(ctx.Request.Method) &&
                !ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/health"))
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                await ctx.Response.SendFileAsync(files.GetFileInfo("index.html"));
                return;
            }

            await next();
        });
    }
}
