using Integrations.Application.Contracts;
using SharedKernel;

namespace Integrations.Application.Connections;

public enum ConnectionStatus
{
    Active = 0,
    NeedsAttention = 1,
    Disabled = 2,
}

/// <summary>
/// A configured read-only link to one broker account. Credentials are encrypted with Data Protection and are
/// never returned by the API, logged, or exposed to AI clients.
/// </summary>
public sealed class BrokerConnection : Entity, IAuditable
{
    private BrokerConnection() { }

    public BrokerKind Kind { get; private set; }
    public string DisplayName { get; private set; } = null!;

    /// <summary>The finance account (kind Broker) this connection feeds.</summary>
    public Guid AccountId { get; private set; }
    public string ProtectedCredentials { get; private set; } = null!;
    public ConnectionStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastSuccessfulSyncUtc { get; private set; }
    public DateOnly? CredentialsExpireOn { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static BrokerConnection Create(BrokerKind kind, string displayName, Guid accountId,
        string protectedCredentials, DateOnly? expiresOn) => new()
    {
        Kind = kind,
        DisplayName = displayName.Trim(),
        AccountId = accountId,
        ProtectedCredentials = protectedCredentials,
        CredentialsExpireOn = expiresOn,
    };

    public void ReplaceCredentials(string protectedCredentials, DateOnly? expiresOn)
    {
        ProtectedCredentials = protectedCredentials;
        CredentialsExpireOn = expiresOn;
        Status = ConnectionStatus.Active;
        LastError = null;
    }

    public void Rename(string name) => DisplayName = name.Trim();

    public void Succeeded(DateTimeOffset at)
    {
        LastSuccessfulSyncUtc = at;
        Status = ConnectionStatus.Active;
        LastError = null;
    }

    public void NeedsAttention(string reason)
    {
        Status = ConnectionStatus.NeedsAttention;
        LastError = reason.Length > 500 ? reason[..500] : reason;
    }

    public void TemporaryFailure(string reason) => LastError = reason.Length > 500 ? reason[..500] : reason;

    public void SetEnabled(bool enabled) => Status = enabled ? ConnectionStatus.Active : ConnectionStatus.Disabled;
}

public enum SyncTrigger
{
    Scheduled = 0,
    Manual = 1,
    CsvImport = 2,
}

public enum SyncOutcome
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
}

/// <summary>One synchronisation run: what came in, what changed, what was skipped and why.</summary>
public sealed class SyncJob : Entity
{
    private SyncJob() { }

    public Guid ConnectionId { get; private set; }
    public SyncTrigger Trigger { get; private set; }
    public SyncOutcome Outcome { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public int Imported { get; private set; }
    public int Updated { get; private set; }
    public int Ignored { get; private set; }

    /// <summary>JSON array of messages. Never contains credentials or raw payloads.</summary>
    public string Errors { get; private set; } = "[]";

    public static SyncJob Start(Guid connectionId, SyncTrigger trigger, DateTimeOffset now) =>
        new() { ConnectionId = connectionId, Trigger = trigger, StartedAtUtc = now, Outcome = SyncOutcome.Running };

    public void Finish(SyncOutcome outcome, int imported, int updated, int ignored, string errorsJson,
        DateTimeOffset now)
    {
        Outcome = outcome;
        Imported = imported;
        Updated = updated;
        Ignored = ignored;
        Errors = errorsJson;
        FinishedAtUtc = now;
    }
}
