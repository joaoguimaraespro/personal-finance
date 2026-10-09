using SharedKernel;

namespace Finance.Domain.Categories;

public enum CategoryType
{
    Expense = 0,
    Income = 1,
}

/// <summary>Fixed vs variable — the core split of the original spreadsheet.</summary>
public enum ExpenseNature
{
    Fixed = 0,
    Variable = 1,
}

public sealed class Category : Entity
{
    private Category() { }

    /// <summary>Stable slug. System categories are translated in the UI by key; custom ones show <see cref="Name"/>.</summary>
    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public CategoryType Type { get; private set; }

    /// <summary>Default nature for new expenses; a transaction can override it (the Excel allowed "Other" in both blocks).</summary>
    public ExpenseNature? DefaultNature { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsSystem { get; private set; }
    public string? Color { get; private set; }
    public string? Icon { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public static Category CreateSystem(Guid id, string key, string name, CategoryType type, ExpenseNature? nature,
        Guid? parentId, string color, string icon, int sortOrder) => new()
    {
        Id = id,
        Key = key,
        Name = name,
        Type = type,
        DefaultNature = type == CategoryType.Expense ? nature : null,
        ParentId = parentId,
        IsSystem = true,
        Color = color,
        Icon = icon,
        SortOrder = sortOrder,
    };

    public static Category CreateCustom(string name, CategoryType type, ExpenseNature? nature, Guid? parentId,
        string? color, string? icon) => new()
    {
        // Random, not v7: a v7 GUID starts with the timestamp, so two created in the same millisecond collided.
        Key = "custom-" + Guid.NewGuid().ToString("N")[..12],
        Name = name.Trim(),
        Type = type,
        DefaultNature = type == CategoryType.Expense ? nature ?? ExpenseNature.Variable : null,
        ParentId = parentId,
        Color = color,
        Icon = icon,
        SortOrder = 1000,
    };

    public void Update(string name, ExpenseNature? nature, string? color, string? icon)
    {
        Name = name.Trim();
        if (Type == CategoryType.Expense)
        {
            DefaultNature = nature ?? DefaultNature;
        }

        Color = color;
        Icon = icon;
    }

    public void Archive(DateTimeOffset now) => ArchivedAtUtc = now;

    public void Restore() => ArchivedAtUtc = null;
}
