namespace Finance.Domain.Transactions;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Restored = 3,
}

/// <summary>Append-only change log. Nothing financial is ever changed without a trace.</summary>
public sealed class TransactionAudit
{
    public long Id { get; init; }
    public Guid TransactionId { get; init; }
    public AuditAction Action { get; init; }
    public DateTimeOffset AtUtc { get; init; }
    public string Actor { get; init; } = null!;

    /// <summary>JSON object of changed properties: { "Prop": { "from": x, "to": y } }.</summary>
    public string Changes { get; init; } = "{}";
}
