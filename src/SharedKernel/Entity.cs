namespace SharedKernel;

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; set; }
    DateTimeOffset UpdatedAtUtc { get; set; }
}

public interface ISoftDeletable
{
    DateTimeOffset? DeletedAtUtc { get; }
}
