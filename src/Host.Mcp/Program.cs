using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Ai.Contracts;
using Host.Mcp;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GatewayClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://api:8080");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "personal-finance", Title = "Personal Finance (read-only)", Version = "1.0.0" };
        o.ServerInstructions = AiTools.ServerInstructions;
    })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<FinanceMcpTools>()
    .WithRequestFilters(filters => filters.AddListToolsFilter(next => async (context, ct) =>
    {
        // Minimisation starts at discovery: a client only sees the tools its scopes allow.
        var result = await next(context, ct);
        var caller = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Items["caller"] as CallerInfo;
        var allowed = caller?.Tools.ToHashSet(StringComparer.Ordinal) ?? [];
        result.Tools = result.Tools.Where(t => allowed.Contains(t.Name)).ToList();
        return result;
    }));

var app = builder.Build();

app.UseRateLimiter();
app.MapGet("/healthz", () => Results.Ok());

// Every MCP request must carry a valid, unrevoked client token (checked against the gateway, cached briefly).
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/mcp"), branch => branch.Use(async (ctx, next) =>
{
    var token = BearerToken.From(ctx);
    if (token is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return;
    }

    var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();
    var key = "caller:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    if (!cache.TryGetValue(key, out CallerInfo? caller))
    {
        caller = await ctx.RequestServices.GetRequiredService<GatewayClient>().WhoAmIAsync(token, ctx.RequestAborted);
        // Short cache: a revoked client loses access within seconds; the gateway re-checks every tool call anyway.
        cache.Set(key, caller, TimeSpan.FromSeconds(15));
    }

    if (caller is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    ctx.Items["caller"] = caller;
    await next();
}));

app.MapMcp("/mcp");

await app.RunAsync();
