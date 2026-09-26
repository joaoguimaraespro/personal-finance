using SharedKernel;

namespace Ai.Application.Domain;

/// <summary>
/// One AI integration (Claude Code, ChatGPT, a local model…) with its own token, scopes, limits and audit trail.
/// Only a SHA-256 hash of the token secret is stored; the token itself is shown once, at creation.
/// </summary>
public sealed class AiClient : Entity
{
    private AiClient() { }

    public string Name { get; private set; } = null!;

    /// <summary>Public, non-secret part of the token used to look the client up (e.g. "pf_1a2b3c4d").</summary>
    public string TokenPrefix { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public List<string> Scopes { get; private set; } = [];
    public int RateLimitPerMinute { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    /// <summary>In-process client used by the in-app assistant. Its token is never issued to anyone.</summary>
    public bool Internal { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);

    public static AiClient Create(string name, string prefix, string hash, IEnumerable<string> scopes, int rateLimit,
        DateTimeOffset now, DateTimeOffset? expiresAt) => new()
    {
        Name = name.Trim(),
        TokenPrefix = prefix,
        TokenHash = hash,
        Scopes = scopes.Distinct().Order().ToList(),
        RateLimitPerMinute = rateLimit,
        CreatedAtUtc = now,
        ExpiresAtUtc = expiresAt,
    };

    public static AiClient CreateInternal(string name, string prefix, string hash, IEnumerable<string> scopes,
        DateTimeOffset now)
    {
        var client = Create(name, prefix, hash, scopes, 30, now, null);
        client.Internal = true;
        return client;
    }

    public void SetScopes(IEnumerable<string> scopes) => Scopes = scopes.Distinct().Order().ToList();

    public void SetRateLimit(int perMinute) => RateLimitPerMinute = perMinute;

    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;

    public void Touch(DateTimeOffset now) => LastUsedAtUtc = now;
}

public enum AiDecision
{
    Allowed = 0,
    Denied = 1,
    RateLimited = 2,
    Unauthenticated = 3,
    Invalid = 4,
    Failed = 5,
}

/// <summary>
/// Record of every AI tool call, allowed or not. Stores what was asked and how much came back — never the
/// financial values themselves.
/// </summary>
public sealed class AiAuditEvent
{
    public long Id { get; init; }
    public Guid? ClientId { get; init; }
    public string? ClientName { get; init; }
    public string Tool { get; init; } = null!;
    public string? Scope { get; init; }
    public AiDecision Decision { get; init; }

    /// <summary>Validated, allow-listed arguments only (e.g. {"period":"2026-09","category":"restaurants"}).</summary>
    public string Arguments { get; init; } = "{}";
    public int RecordCount { get; init; }
    public int ResponseBytes { get; init; }
    public int DurationMs { get; init; }
    public DateTimeOffset AtUtc { get; init; }
    public string? Reason { get; init; }
}
