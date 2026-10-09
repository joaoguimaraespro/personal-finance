using SharedKernel;

namespace Finance.Domain.Allocation;

public enum BucketGroup
{
    Investment = 0,
    Savings = 1,
}

/// <summary>
/// Where income is set aside: the spreadsheet's "Ações / ETFs", "Crypto", "Férias", "Outras Poupanças" rows.
/// </summary>
public sealed class AllocationBucket : Entity
{
    private AllocationBucket() { }

    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public BucketGroup Group { get; private set; }
    public bool IsSystem { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public static AllocationBucket CreateSystem(Guid id, string key, string name, BucketGroup group, int sortOrder) =>
        new() { Id = id, Key = key, Name = name, Group = group, IsSystem = true, SortOrder = sortOrder };

    public static AllocationBucket CreateCustom(string name, BucketGroup group) => new()
    {
        // Random, not v7: a v7 GUID starts with the timestamp, so two created in the same millisecond collided.
        Key = "custom-" + Guid.NewGuid().ToString("N")[..12],
        Name = name.Trim(),
        Group = group,
        SortOrder = 1000,
    };

    public void Rename(string name) => Name = name.Trim();

    public void Archive(DateTimeOffset now) => ArchivedAtUtc = now;
}

public enum AllocationStatus
{
    Todo = 0,
    Done = 1,
    Partial = 2,
    NotApplicable = 3,
}

/// <summary>The spreadsheet's per-month "Estado" checklist (Feito / Por fazer / Parcial / N/A).</summary>
public sealed class AllocationCheck
{
    private AllocationCheck() { }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public Guid BucketId { get; private set; }
    public AllocationStatus Status { get; private set; }

    public YearMonth Period => new(Year, Month);

    public static AllocationCheck Create(YearMonth period, Guid bucketId, AllocationStatus status) =>
        new() { Year = period.Year, Month = period.Month, BucketId = bucketId, Status = status };

    public void Set(AllocationStatus status) => Status = status;
}
