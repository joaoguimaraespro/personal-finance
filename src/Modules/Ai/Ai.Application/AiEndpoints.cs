using System.Text.Json;
using Ai.Application.Domain;
using Ai.Application.Gateway;
using Ai.Application.Tools;
using Ai.Contracts;
using FluentValidation;
using Finance.Application.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace Ai.Application;

public sealed record CreateAiClientRequest(string Name, List<string> Scopes, int? ExpiresInDays, int? RateLimitPerMinute);

public sealed record UpdateAiClientRequest(List<string> Scopes, int? RateLimitPerMinute);

public sealed record AiClientDto(Guid Id, string Name, string TokenPrefix, IReadOnlyList<string> Scopes, int RateLimitPerMinute,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, DateTimeOffset? LastUsedAtUtc,
    int CallsLast24h, int DeniedLast24h);

public sealed class CreateAiClientValidator : AbstractValidator<CreateAiClientRequest>
{
    public CreateAiClientValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Scopes).NotNull().Must(s => s.All(AiScopes.Names.Contains)).WithMessage("Unknown scope.");
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 730).When(x => x.ExpiresInDays is not null);
        RuleFor(x => x.RateLimitPerMinute).InclusiveBetween(1, 600).When(x => x.RateLimitPerMinute is not null);
    }
}

public static class AiEndpoints
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateAiClientValidator>();
        services.AddScoped<FinanceTools>();
        services.AddScoped<AiGateway>();
        services.AddSingleton<AiRateLimiter>();
        return services;
    }

    /// <summary>
    /// Bearer-token endpoints used by the MCP server and the assistant. They never look at the browser session, so
    /// a logged-in cookie grants nothing here, and they can only run catalogued read-only tools.
    /// </summary>
    public static IEndpointRouteBuilder MapAiGateway(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/ai").WithTags("AI gateway").AllowAnonymous();

        group.MapGet("/whoami", async (HttpContext http, AiGateway gateway, CancellationToken ct) =>
            await gateway.WhoAmIAsync(Bearer(http), ct) is { } me ? Results.Ok(me) : Results.Unauthorized());

        group.MapPost("/tools/{name}", async (string name, HttpContext http, AiGateway gateway, CancellationToken ct) =>
        {
            // Read the body whatever its framing (Content-Length or chunked), capped: tool arguments are tiny.
            const int maxArgumentBytes = 8 * 1024;
            if (http.Request.ContentLength > maxArgumentBytes)
            {
                return Results.BadRequest(new { error = "Arguments are too large." });
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxArgumentBytes)
                {
                    return Results.BadRequest(new { error = "Arguments are too large." });
                }
            }

            JsonElement args = default;
            if (buffer.Length > 0)
            {
                try
                {
                    args = JsonSerializer.Deserialize<JsonElement>(buffer.ToArray());
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Arguments must be a JSON object." });
                }

                if (args.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                {
                    return Results.BadRequest(new { error = "Arguments must be a JSON object." });
                }
            }

            var result = await gateway.InvokeAsync(Bearer(http), name, args, ct);
            return Results.Json(result.Body, AiGateway.Json, statusCode: result.Status);
        });

        return api;
    }

    /// <summary>Owner-only management (cookie + MFA session): create, scope, revoke clients and read the audit log.</summary>
    public static IEndpointRouteBuilder MapAiManagement(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/ai-admin").WithTags("AI clients");

        group.MapGet("/catalog", () => new { scopes = AiScopes.All, tools = AiTools.All });

        group.MapGet("/clients", async (IAiDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var since = clock.GetUtcNow().AddDays(-1);
            var clients = await db.Clients.AsNoTracking().OrderBy(c => c.RevokedAtUtc != null).ThenBy(c => c.Name).ToListAsync(ct);
            var usage = await db.AuditEvents.AsNoTracking().Where(e => e.AtUtc >= since && e.ClientId != null)
                .GroupBy(e => e.ClientId!.Value)
                .Select(g => new { Id = g.Key, Calls = g.Count(), Denied = g.Count(e => e.Decision != AiDecision.Allowed) })
                .ToListAsync(ct);
            return clients.Select(c =>
            {
                var u = usage.FirstOrDefault(x => x.Id == c.Id);
                return new AiClientDto(c.Id, c.Name, c.TokenPrefix, c.Scopes, c.RateLimitPerMinute, c.CreatedAtUtc,
                    c.ExpiresAtUtc, c.RevokedAtUtc, c.LastUsedAtUtc, u?.Calls ?? 0, u?.Denied ?? 0);
            });
        });

        // The token is returned exactly once. Only its hash is stored.
        group.MapPost("/clients", async (CreateAiClientRequest req, IAiDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var (token, prefix, hash) = AiTokens.Generate();
            var now = clock.GetUtcNow();
            var client = AiClient.Create(req.Name, prefix, hash, req.Scopes, req.RateLimitPerMinute ?? 60, now,
                req.ExpiresInDays is { } days ? now.AddDays(days) : null);
            db.Clients.Add(client);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ai-admin/clients/{client.Id}", new { client.Id, token });
        }).Validate<CreateAiClientRequest>();

        group.MapPut("/clients/{id:guid}", async (Guid id, UpdateAiClientRequest req, IAiDb db, CancellationToken ct) =>
        {
            if (!req.Scopes.All(AiScopes.Names.Contains) || req.RateLimitPerMinute is < 1 or > 600)
            {
                return ResultHttp.Problem(Error.Validation("AiClient.Scopes", "Unknown scope or invalid rate limit."));
            }

            var client = await db.Clients.FindAsync([id], ct);
            if (client is null || client.RevokedAtUtc is not null)
            {
                return ResultHttp.Problem(Error.NotFound("AiClient.NotFound", "Client not found or revoked."));
            }

            client.SetScopes(req.Scopes);
            if (req.RateLimitPerMinute is { } limit)
            {
                client.SetRateLimit(limit);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/clients/{id:guid}/revoke", async (Guid id, IAiDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var client = await db.Clients.FindAsync([id], ct);
            if (client is null)
            {
                return ResultHttp.Problem(Error.NotFound("AiClient.NotFound", "Client not found."));
            }

            client.Revoke(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/audit", async (IAiDb db, Guid? clientId, int? limit, CancellationToken ct) =>
            await db.AuditEvents.AsNoTracking()
                .Where(e => clientId == null || e.ClientId == clientId)
                .OrderByDescending(e => e.Id)
                .Take(Math.Clamp(limit ?? 100, 1, 500))
                .ToListAsync(ct));

        return api;
    }

    private static string? Bearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..].Trim() : null;
    }
}
