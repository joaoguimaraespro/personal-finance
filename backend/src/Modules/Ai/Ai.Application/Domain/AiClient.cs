using SharedKernel;

namespace Ai.Application.Domain;

/// <summary>
/// One AI integration (Claude Code, ChatGPT, a local model…) with its own token, scopes, limits and audit trail.
/// Only a SHA-256 hash of the token secret is stored; the token itself is shown once, at creation.
/// </summary>
public sealed class AiClient : Entity
{
    public const int DefaultWritesPerHour = 20;

    private AiClient() { }

    public string Name { get; private set; } = null!;

    /// <summary>Public, non-secret part of the token used to look the client up (e.g. "pf_1a2b3c4d").</summary>
    public string TokenPrefix { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public List<string> Scopes { get; private set; } = [];
    public int RateLimitPerMinute { get; private set; }

    /// <summary>Separate, low budget for write tools (ADR-0008), on top of the per-minute limit.</summary>
    public int WritesPerHour { get; private set; } = DefaultWritesPerHour;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    /// <summary>In-process client used by the in-app assistant. Its token is never issued to anyone.</summary>
    public bool Internal { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);

    public static AiClient Create(string name, string prefix, string hash, IEnumerable<string> scopes, int rateLimit,
        DateTimeOffset now, DateTimeOffset? expiresAt, int writesPerHour = DefaultWritesPerHour) => new()
    {
        Name = name.Trim(),
        TokenPrefix = prefix,
        TokenHash = hash,
        Scopes = scopes.Distinct().Order().ToList(),
        RateLimitPerMinute = rateLimit,
        WritesPerHour = writesPerHour,
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

    public void SetWritesPerHour(int perHour) => WritesPerHour = perHour;

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
/// financial values a read returned. A write also records the record it changed and a short before/after summary.
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

    /// <summary>True for write tools, whatever the decision.</summary>
    public bool Write { get; init; }

    /// <summary>The record a write created or changed (transaction, goal, holding…).</summary>
    public Guid? RecordId { get; init; }

    /// <summary>Before/after summary of an allowed write, e.g. {"before":{"amount":12.5},"after":{"amount":15}}.</summary>
    public string? Changes { get; init; }
}

/// <summary>
/// A record an AI client deleted. AI deletes are soft: the record stays hidden and restorable from the recycle bin
/// for <see cref="RetentionDays"/> days, after which the purge job removes it for good.
/// </summary>
public sealed class AiRecycledItem : Entity
{
    public const int RetentionDays = 30;
    public const string TransactionKind = "transaction";

    private AiRecycledItem() { }

    public string Kind { get; private set; } = null!;
    public Guid RecordId { get; private set; }
    public Guid? ClientId { get; private set; }
    public string ClientName { get; private set; } = null!;

    /// <summary>What was deleted, for the owner (e.g. "Expense · 2026-10-01 · 12.50 EUR · Groceries · Lunch").</summary>
    public string Summary { get; private set; } = null!;
    public DateTimeOffset DeletedAtUtc { get; private set; }
    public DateTimeOffset PurgeAfterUtc { get; private set; }
    public DateTimeOffset? RestoredAtUtc { get; private set; }
    public DateTimeOffset? PurgedAtUtc { get; private set; }

    public bool InBin => RestoredAtUtc is null && PurgedAtUtc is null;

    public static AiRecycledItem Create(string kind, Guid recordId, AiClient client, string summary,
        DateTimeOffset deletedAt) => new()
    {
        Kind = kind,
        RecordId = recordId,
        ClientId = client.Id,
        ClientName = client.Name,
        Summary = summary.Length > 300 ? summary[..300] : summary,
        DeletedAtUtc = deletedAt,
        PurgeAfterUtc = deletedAt.AddDays(RetentionDays),
    };

    public void MarkRestored(DateTimeOffset now) => RestoredAtUtc ??= now;

    public void MarkPurged(DateTimeOffset now) => PurgedAtUtc ??= now;
}

/// <summary>
/// Remembers a write made with an idempotency key, so a retried call with the same key returns the first result
/// instead of writing twice. Kept for a day.
/// </summary>
public sealed class AiWriteReceipt
{
    public const int RetentionHours = 24;

    public long Id { get; init; }
    public Guid ClientId { get; init; }
    public string Key { get; init; } = null!;
    public string Tool { get; init; } = null!;

    /// <summary>SHA-256 of the validated arguments: the same key with different arguments is refused.</summary>
    public string ArgumentsHash { get; init; } = null!;

    /// <summary>The tool's result (record id and summary) as returned the first time.</summary>
    public string Result { get; init; } = "{}";
    public DateTimeOffset AtUtc { get; init; }
}
