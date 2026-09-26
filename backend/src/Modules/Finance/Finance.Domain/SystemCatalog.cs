using System.Security.Cryptography;
using System.Text;
using Finance.Domain.Allocation;
using Finance.Domain.Categories;

namespace Finance.Domain;

/// <summary>
/// Built-in categories and buckets with deterministic ids so seeds, imports and tests agree across databases.
/// Labels are translated in the UI by <c>Key</c>; <c>Name</c> is the English fallback.
/// </summary>
public static class SystemCatalog
{
    private static readonly (string Key, string Name, CategoryType Type, ExpenseNature? Nature, string? Parent, string Color, string Icon)[] CategoryDefs =
    [
        ("housing", "Housing", CategoryType.Expense, ExpenseNature.Fixed, null, "#6366f1", "home"),
        ("utilities", "Utilities", CategoryType.Expense, ExpenseNature.Fixed, null, "#0ea5e9", "bolt"),
        ("electricity", "Electricity", CategoryType.Expense, ExpenseNature.Fixed, "utilities", "#0ea5e9", "bolt"),
        ("water-gas", "Water & Gas", CategoryType.Expense, ExpenseNature.Fixed, "utilities", "#0ea5e9", "droplet"),
        ("internet", "Internet", CategoryType.Expense, ExpenseNature.Fixed, "utilities", "#0ea5e9", "wifi"),
        ("mobile", "Mobile Phone", CategoryType.Expense, ExpenseNature.Fixed, "utilities", "#0ea5e9", "phone"),
        ("groceries", "Groceries", CategoryType.Expense, ExpenseNature.Variable, null, "#22c55e", "cart"),
        ("restaurants", "Restaurants", CategoryType.Expense, ExpenseNature.Variable, null, "#f97316", "utensils"),
        ("food", "Food", CategoryType.Expense, ExpenseNature.Variable, null, "#84cc16", "apple"),
        ("transport", "Transport", CategoryType.Expense, ExpenseNature.Variable, null, "#eab308", "bus"),
        ("fuel", "Fuel", CategoryType.Expense, ExpenseNature.Variable, "transport", "#eab308", "fuel"),
        ("travel", "Travel", CategoryType.Expense, ExpenseNature.Variable, null, "#14b8a6", "plane"),
        ("entertainment", "Entertainment", CategoryType.Expense, ExpenseNature.Variable, null, "#ec4899", "ticket"),
        ("subscriptions", "Subscriptions", CategoryType.Expense, ExpenseNature.Fixed, null, "#a855f7", "repeat"),
        ("gym", "Gym", CategoryType.Expense, ExpenseNature.Fixed, null, "#a855f7", "dumbbell"),
        ("shopping", "Shopping", CategoryType.Expense, ExpenseNature.Variable, null, "#f43f5e", "bag"),
        ("clothing", "Clothing", CategoryType.Expense, ExpenseNature.Variable, "shopping", "#f43f5e", "shirt"),
        ("health", "Health", CategoryType.Expense, ExpenseNature.Variable, null, "#ef4444", "heart"),
        ("education", "Education", CategoryType.Expense, ExpenseNature.Variable, null, "#3b82f6", "book"),
        ("insurance", "Insurance", CategoryType.Expense, ExpenseNature.Fixed, null, "#64748b", "shield"),
        ("health-insurance", "Health Insurance", CategoryType.Expense, ExpenseNature.Fixed, "insurance", "#64748b", "shield"),
        ("car-insurance", "Car Insurance", CategoryType.Expense, ExpenseNature.Fixed, "insurance", "#64748b", "car"),
        ("taxes", "Taxes", CategoryType.Expense, ExpenseNature.Variable, null, "#78716c", "landmark"),
        ("gifts", "Gifts", CategoryType.Expense, ExpenseNature.Variable, null, "#d946ef", "gift"),
        ("other", "Other", CategoryType.Expense, ExpenseNature.Variable, null, "#94a3b8", "dots"),
        ("salary", "Salary", CategoryType.Income, null, null, "#16a34a", "briefcase"),
        ("bonus", "Bonus", CategoryType.Income, null, null, "#16a34a", "star"),
        ("freelance", "Freelance", CategoryType.Income, null, null, "#16a34a", "laptop"),
        ("interest", "Interest", CategoryType.Income, null, null, "#16a34a", "percent"),
        ("dividends", "Dividends", CategoryType.Income, null, null, "#16a34a", "trending-up"),
        ("other-income", "Other Income", CategoryType.Income, null, null, "#16a34a", "plus"),
    ];

    private static readonly (string Key, string Name, BucketGroup Group)[] BucketDefs =
    [
        ("stocks-etfs", "Stocks / ETFs", BucketGroup.Investment),
        ("crypto", "Crypto", BucketGroup.Investment),
        ("bonds", "Bonds", BucketGroup.Investment),
        ("travel-fund", "Travel fund", BucketGroup.Savings),
        ("emergency-fund", "Emergency fund", BucketGroup.Savings),
        ("other-savings", "Other savings", BucketGroup.Savings),
    ];

    public static Guid IdFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("personal-finance:" + key)).AsSpan(0, 16).ToArray();
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80); // RFC 9562 version 8 (custom, name-based)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, bigEndian: true);
    }

    public static IReadOnlyList<Category> Categories() =>
        CategoryDefs.Select((c, i) => Category.CreateSystem(IdFor("category:" + c.Key), c.Key, c.Name, c.Type,
            c.Nature, c.Parent is null ? null : IdFor("category:" + c.Parent), c.Color, c.Icon, i)).ToList();

    public static IReadOnlyList<AllocationBucket> Buckets() =>
        BucketDefs.Select((b, i) => AllocationBucket.CreateSystem(IdFor("bucket:" + b.Key), b.Key, b.Name, b.Group, i))
            .ToList();

    public static Guid CategoryId(string key) => IdFor("category:" + key);

    public static Guid BucketId(string key) => IdFor("bucket:" + key);
}
