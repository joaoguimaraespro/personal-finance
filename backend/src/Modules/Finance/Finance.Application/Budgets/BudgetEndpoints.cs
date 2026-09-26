using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Budgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Budgets;

public sealed record BudgetItemDto(BudgetTarget Target, BudgetMode Mode, decimal Value, Guid? BucketId, Guid? CategoryId);

public sealed record BudgetDto(Guid Id, string EffectiveFrom, string? Note, IReadOnlyList<BudgetItemDto> Items);

public sealed record SaveBudgetRequest(string? Note, IReadOnlyList<BudgetItemDto> Items);

public sealed class SaveBudgetValidator : AbstractValidator<SaveBudgetRequest>
{
    public SaveBudgetValidator()
    {
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Items).NotNull();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Target).IsInEnum();
            item.RuleFor(i => i.Mode).IsInEnum();
            item.RuleFor(i => i.BucketId).NotNull().When(i => i.Target == BudgetTarget.Bucket);
            item.RuleFor(i => i.CategoryId).NotNull().When(i => i.Target == BudgetTarget.Category);
            item.RuleFor(i => i.Mode).NotEqual(BudgetMode.Remainder).When(i => i.Target == BudgetTarget.Category)
                .WithMessage("Category budgets must be a percentage or a fixed amount.");
        });
    }
}

public static class BudgetEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Budget.NotFound", "No budget is defined for this period.");
    private static readonly Error BadPeriod = Error.Validation("Period", "Use yyyy-MM.");

    public static void MapBudgets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/budgets").WithTags("Budgets");

        group.MapGet("/", async (IFinanceDb db, CancellationToken ct) =>
        {
            var budgets = await db.Budgets.AsNoTracking().Include(b => b.Items)
                .OrderByDescending(b => b.EffectiveFrom).ToListAsync(ct);
            return Results.Ok(budgets.Select(ToDto));
        });

        group.MapGet("/effective/{period}", async (string period, IFinanceDb db, CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(BadPeriod);
            }

            var budget = await FindEffectiveAsync(db, ym, ct);
            return budget is null ? ResultHttp.Problem(NotFound) : Results.Ok(ToDto(budget));
        });

        // PUT creates or replaces the version that starts in {period}; earlier months keep their own version.
        group.MapPut("/{period}", async (string period, SaveBudgetRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(BadPeriod);
            }

            var specs = req.Items.Select(i => new BudgetItemSpec(i.Target, i.Mode, i.Value, i.BucketId, i.CategoryId))
                .ToList();
            var existing = await db.Budgets.Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.EffectiveFrom == ym.FirstDay, ct);
            if (existing is null)
            {
                var created = Budget.Create(ym, specs, req.Note);
                if (created.IsFailure)
                {
                    return ResultHttp.Problem(created.Error);
                }

                db.Budgets.Add(created.Value);
            }
            else
            {
                var replaced = existing.ReplaceItems(specs);
                if (replaced.IsFailure)
                {
                    return ResultHttp.Problem(replaced.Error);
                }

                existing.SetNote(req.Note);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<SaveBudgetRequest>();

        group.MapDelete("/{period}", async (string period, IFinanceDb db, CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(BadPeriod);
            }

            var budget = await db.Budgets.FirstOrDefaultAsync(b => b.EffectiveFrom == ym.FirstDay, ct);
            if (budget is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            db.Budgets.Remove(budget);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>The latest version whose EffectiveFrom is on or before the month.</summary>
    public static Task<Budget?> FindEffectiveAsync(IFinanceDb db, YearMonth period, CancellationToken ct) =>
        db.Budgets.AsNoTracking().Include(b => b.Items)
            .Where(b => b.EffectiveFrom <= period.FirstDay)
            .OrderByDescending(b => b.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    private static BudgetDto ToDto(Budget b) => new(b.Id, YearMonth.From(b.EffectiveFrom).ToString(), b.Note,
        b.Items.Select(i => new BudgetItemDto(i.Target, i.Mode, i.Value, i.BucketId, i.CategoryId)).ToList());
}
