using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Goals;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Goals;

public sealed record GoalDto(
    Guid Id,
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    decimal StartingAmount,
    decimal? ManualCurrentAmount,
    decimal CurrentAmount,
    decimal Progress,
    decimal Remaining,
    decimal? MonthlyNeeded,
    string? Icon,
    bool Achieved,
    bool Archived);

public sealed record GoalRequest(string Name, decimal TargetAmount, DateOnly? TargetDate, decimal? StartingAmount,
    decimal? ManualCurrentAmount, string? Icon);

public sealed class GoalRequestValidator : AbstractValidator<GoalRequest>
{
    public GoalRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.TargetAmount).GreaterThan(0);
        RuleFor(x => x.StartingAmount).GreaterThanOrEqualTo(0).When(x => x.StartingAmount is not null);
        RuleFor(x => x.ManualCurrentAmount).GreaterThanOrEqualTo(0).When(x => x.ManualCurrentAmount is not null);
        RuleFor(x => x.Icon).MaximumLength(40);
    }
}

public static class GoalEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Goal.NotFound", "Goal not found.");

    public static void MapGoals(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/goals").WithTags("Goals");

        group.MapGet("/", async (IFinanceDb db, TimeProvider clock, bool? includeArchived, CancellationToken ct) =>
            Results.Ok(await ListAsync(db, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), includeArchived == true,
                ct)));

        group.MapPost("/", async (GoalRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var goal = await CreateAsync(db, req, ct);
            return Results.Created($"/api/goals/{goal.Id}", new { goal.Id });
        }).Validate<GoalRequest>();

        group.MapPut("/{id:guid}", async (Guid id, GoalRequest req, IFinanceDb db, CancellationToken ct) =>
            (await UpdateAsync(db, id, req, ct)).ToHttp(_ => Results.NoContent())).Validate<GoalRequest>();

        group.MapPost("/{id:guid}/archive", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var goal = await db.Goals.FindAsync([id], ct);
            if (goal is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            goal.Archive(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public static async Task<FinancialGoal> CreateAsync(IFinanceDb db, GoalRequest req, CancellationToken ct)
    {
        var goal = FinancialGoal.Create(req.Name, req.TargetAmount, req.TargetDate, req.StartingAmount ?? 0,
            req.ManualCurrentAmount, req.Icon);
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        return goal;
    }

    public static async Task<Result<FinancialGoal>> UpdateAsync(IFinanceDb db, Guid id, GoalRequest req,
        CancellationToken ct)
    {
        var goal = await db.Goals.FindAsync([id], ct);
        if (goal is null)
        {
            return NotFound;
        }

        goal.Update(req.Name, req.TargetAmount, req.TargetDate, req.StartingAmount ?? 0, req.ManualCurrentAmount,
            req.Icon);
        await db.SaveChangesAsync(ct);
        return goal;
    }

    public static async Task<IReadOnlyList<GoalDto>> ListAsync(IFinanceDb db, DateOnly today, bool includeArchived,
        CancellationToken ct)
    {
        var goals = await db.Goals.AsNoTracking()
            .Where(g => includeArchived || g.ArchivedAtUtc == null)
            .OrderBy(g => g.TargetDate ?? DateOnly.MaxValue).ThenBy(g => g.Name)
            .ToListAsync(ct);
        var contributions = await db.Transactions.AsNoTracking()
            .Where(t => t.GoalId != null && t.Type == TransactionType.Savings)
            .GroupBy(t => t.GoalId!.Value)
            .Select(g => new { GoalId = g.Key, Total = g.Sum(t => t.BaseAmount) })
            .ToDictionaryAsync(x => x.GoalId, x => x.Total, ct);

        return goals.Select(g =>
        {
            var current = g.CurrentAmount(contributions.GetValueOrDefault(g.Id));
            var remaining = Math.Max(0, g.TargetAmount - current);
            decimal? monthly = null;
            if (g.TargetDate is { } target && remaining > 0)
            {
                var months = Math.Max(1, (target.Year - today.Year) * 12 + target.Month - today.Month);
                monthly = Currency.Round(remaining / months);
            }

            return new GoalDto(g.Id, g.Name, g.TargetAmount, g.TargetDate, g.StartingAmount, g.ManualCurrentAmount,
                current, g.TargetAmount == 0 ? 0 : decimal.Round(current / g.TargetAmount, 4), remaining, monthly,
                g.Icon, current >= g.TargetAmount, g.ArchivedAtUtc is not null);
        }).ToList();
    }
}
