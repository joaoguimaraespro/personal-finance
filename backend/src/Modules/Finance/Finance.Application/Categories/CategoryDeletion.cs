using Finance.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Categories;

/// <summary>What deleting a category touches: what must move to another one, and its sub-categories.</summary>
public sealed record CategoryDeletionPreview(Guid Id, string Name, bool IsSystem, int Transactions, int Recurring,
    int Subcategories, int BudgetLimits);

/// <summary>
/// Deleting a category for good, unlike archiving it. Its movements and recurring items (whole or one split line)
/// move to another category of the same kind, its sub-categories move up a level and its budget limits go. Built-in
/// categories come back with the app, so they can only be archived (or renamed).
/// </summary>
public static class CategoryDeletion
{
    public static readonly Error BuiltIn = Error.Validation("Category.Delete",
        "Built-in categories can be renamed or archived, not deleted.");

    public static readonly Error MoveRequired = Error.Validation("Category.MoveTo",
        "This category is in use: choose another category of the same kind to move it to.");

    public static async Task<Result<CategoryDeletionPreview>> PreviewAsync(IFinanceDb db, Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return CategoryEndpoints.NotFound;
        }

        return new CategoryDeletionPreview(category.Id, category.Name, category.IsSystem,
            await db.Transactions.IgnoreQueryFilters()
                .CountAsync(t => t.CategoryId == id || t.Splits.Any(s => s.CategoryId == id), ct),
            await db.RecurringTransactions.CountAsync(r => r.CategoryId == id || r.Splits.Any(s => s.CategoryId == id), ct),
            await db.Categories.CountAsync(c => c.ParentId == id, ct),
            await db.Budgets.SelectMany(b => b.Items).CountAsync(i => i.CategoryId == id, ct));
    }

    public static async Task<Result<CategoryDeletionPreview>> DeleteAsync(IFinanceDb db, Guid id, Guid? moveTo,
        CancellationToken ct)
    {
        var preview = await PreviewAsync(db, id, ct);
        if (preview.IsFailure)
        {
            return preview;
        }

        var p = preview.Value;
        if (p.IsSystem)
        {
            return BuiltIn;
        }

        var category = await db.Categories.FirstAsync(c => c.Id == id, ct);
        var target = moveTo is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(c => c.Id == moveTo && c.Id != id && c.ArchivedAtUtc == null &&
                                                          c.Type == category.Type, ct);
        if ((p.Transactions > 0 || p.Recurring > 0) && target is null)
        {
            return MoveRequired;
        }

        foreach (var t in await db.Transactions.IgnoreQueryFilters()
                     .Where(t => t.CategoryId == id || t.Splits.Any(s => s.CategoryId == id)).ToListAsync(ct))
        {
            t.MoveCategory(id, target!.Id);
        }

        foreach (var r in await db.RecurringTransactions
                     .Where(r => r.CategoryId == id || r.Splits.Any(s => s.CategoryId == id)).ToListAsync(ct))
        {
            r.MoveCategory(id, target!.Id);
        }

        foreach (var child in await db.Categories.Where(c => c.ParentId == id).ToListAsync(ct))
        {
            child.MoveUnder(category.ParentId);
        }

        foreach (var budget in await db.Budgets.Include(b => b.Items)
                     .Where(b => b.Items.Any(i => i.CategoryId == id)).ToListAsync(ct))
        {
            budget.RemoveTargetsOf(id);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return p;
    }
}
