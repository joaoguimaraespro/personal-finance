using SharedKernel;

namespace Finance.Domain.Imports;

public enum ImportStatus
{
    Previewed = 0,
    Committed = 1,
    RolledBack = 2,
    Failed = 3,
}

/// <summary>
/// One import run. Every transaction it creates carries its id, so an import can be audited and undone as a unit.
/// The uploaded file itself is never stored — only the parsed, staged values.
/// </summary>
public sealed class ImportBatch : Entity
{
    private ImportBatch() { }

    public string Kind { get; private set; } = null!;
    public DataSource Source { get; private set; }
    public string FileName { get; private set; } = null!;
    public string FileSha256 { get; private set; } = null!;
    public ImportStatus Status { get; private set; }

    /// <summary>Parsed rows, mapping, validation and reconciliation results (jsonb).</summary>
    public string Payload { get; private set; } = "{}";
    public int Created { get; private set; }
    public int Skipped { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CommittedAtUtc { get; private set; }
    public DateTimeOffset? RolledBackAtUtc { get; private set; }

    public static ImportBatch Start(string kind, DataSource source, string fileName, string sha256, string payload,
        DateTimeOffset now) => new()
    {
        Kind = kind,
        Source = source,
        FileName = fileName,
        FileSha256 = sha256,
        Payload = payload,
        Status = ImportStatus.Previewed,
        CreatedAtUtc = now,
    };

    public void UpdatePayload(string payload) => Payload = payload;

    public void MarkCommitted(int created, int skipped, DateTimeOffset now)
    {
        Status = ImportStatus.Committed;
        Created = created;
        Skipped = skipped;
        CommittedAtUtc = now;
    }

    public void MarkRolledBack(DateTimeOffset now)
    {
        Status = ImportStatus.RolledBack;
        RolledBackAtUtc = now;
    }
}
