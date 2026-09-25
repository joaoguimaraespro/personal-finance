using Microsoft.AspNetCore.Antiforgery;

namespace Host.Api.Infrastructure;

/// <summary>
/// CSRF protection for the cookie-authenticated SPA API: every state-changing /api request must echo the
/// XSRF-TOKEN cookie in the X-XSRF-TOKEN header. Bearer-token AI endpoints never use cookies and are excluded.
/// </summary>
internal sealed class AntiforgeryMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    private static readonly PathString[] Excluded = ["/api/ai"];

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var unsafeMethod = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
                             HttpMethods.IsOptions(request.Method));
        if (unsafeMethod && request.Path.StartsWithSegments("/api") &&
            !Excluded.Any(p => request.Path.StartsWithSegments(p)))
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Antiforgery",
                    detail = "Missing or invalid anti-forgery token.",
                    status = 400,
                });
                return;
            }
        }

        await next(context);
    }
}
