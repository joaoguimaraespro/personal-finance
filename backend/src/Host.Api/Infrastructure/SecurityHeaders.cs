namespace Host.Api.Infrastructure;

internal static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            }

            await next();
        });
}
