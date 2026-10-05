using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain;
using Finance.Domain.Categories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Key,
    string Name,
    CategoryType Type,
    ExpenseNature? DefaultNature,
    Guid? ParentId,
    bool IsSystem,
    string? Color,
    string? Icon,
    bool Archived,
    // A built-in category the owner renamed: show its name instead of the translated label.
    bool Renamed);

public sealed record CreateCategoryRequest(string Name, CategoryType Type, ExpenseNature? DefaultNature, Guid? ParentId,
    string? Color, string? Icon);

public sealed record UpdateCategoryRequest(string Name, ExpenseNature? DefaultNature, string? Color, string? Icon);

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Color).Matches("^#[0-9a-fA-F]{6}$").When(x => x.Color is not null);
        RuleFor(x => x.Icon).MaximumLength(40);
    }
}

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Color).Matches("^#[0-9a-fA-F]{6}$").When(x => x.Color is not null);
        RuleFor(x => x.Icon).MaximumLength(40);
    }
}

public static class CategoryEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Category.NotFound", "Category not found.");

    public static void MapCategories(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories");

        group.MapGet("/", async (IFinanceDb db, bool? includeArchived, CancellationToken ct) =>
            Results.Ok((await db.Categories.AsNoTracking()
                    .Where(c => includeArchived == true || c.ArchivedAtUtc == null)
                    .OrderBy(c => c.Type).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .ToListAsync(ct))
                .Select(c => new CategoryDto(c.Id, c.Key, c.Name, c.Type, c.DefaultNature, c.ParentId, c.IsSystem,
                    c.Color, c.Icon, c.ArchivedAtUtc != null,
                    c.IsSystem && SystemCatalog.DefaultCategoryName(c.Key) is { } original && original != c.Name))));

        group.MapPost("/", async (CreateCategoryRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            if (req.ParentId is { } parentId &&
                !await db.Categories.AnyAsync(c => c.Id == parentId && c.Type == req.Type, ct))
            {
                return ResultHttp.Problem(Error.Validation("Category.Parent", "Parent must exist and share the type."));
            }

            var category = Category.CreateCustom(req.Name, req.Type, req.DefaultNature, req.ParentId, req.Color,
                req.Icon);
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/categories/{category.Id}", new { category.Id });
        }).Validate<CreateCategoryRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            category.Update(req.Name, req.DefaultNature, req.Color, req.Icon);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<UpdateCategoryRequest>();

        // Undo a rename of a built-in category: back to the catalogue name (shown translated again).
        group.MapPost("/{id:guid}/reset-name", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (!category.IsSystem || SystemCatalog.DefaultCategoryName(category.Key) is not { } original)
            {
                return ResultHttp.Problem(Error.Validation("Category.NotBuiltIn", "Only built-in categories have a default name."));
            }

            category.Update(original, category.DefaultNature, category.Color, category.Icon);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Categories are archived rather than deleted so historical transactions keep their meaning.
        group.MapPost("/{id:guid}/archive", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            category.Archive(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/restore", async (Guid id, IFinanceDb db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            category.Restore();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
