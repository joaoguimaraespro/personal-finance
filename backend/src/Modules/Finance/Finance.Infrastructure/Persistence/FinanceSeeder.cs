using Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

/// <summary>Inserts missing built-in categories and buckets. Idempotent; never overwrites user edits.</summary>
public static class FinanceSeeder
{
    public static async Task SeedAsync(FinanceDbContext db, CancellationToken ct)
    {
        var existingCategories = await db.Categories.Select(c => c.Id).ToListAsync(ct);
        // Parents first so the self-referencing FK is satisfied.
        foreach (var category in SystemCatalog.Categories().OrderBy(c => c.ParentId.HasValue))
        {
            if (!existingCategories.Contains(category.Id))
            {
                db.Categories.Add(category);
            }
        }

        var existingBuckets = await db.Buckets.Select(b => b.Id).ToListAsync(ct);
        db.Buckets.AddRange(SystemCatalog.Buckets().Where(b => !existingBuckets.Contains(b.Id)));
        await db.SaveChangesAsync(ct);
    }
}
