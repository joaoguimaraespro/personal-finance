using FluentValidation;
using Finance.Application.Http;
using Investments.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Investments.Application.Manual;

public sealed class CreateManualHoldingValidator : AbstractValidator<CreateManualHoldingRequest>
{
    public CreateManualHoldingValidator(TimeProvider clock)
    {
        RuleFor(x => x.CoinId).NotEmpty().WithMessage("Pick a coin from the list.")
            .Matches("^[A-Za-z0-9]{1,30}$").WithMessage("Pick a coin from the list.");
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        ManualHoldingRules.Apply(this, x => x.Quantity, x => x.AveragePrice, x => x.Location, x => x.Notes,
            x => x.HeldSince, clock);
    }
}

public sealed class UpdateManualHoldingValidator : AbstractValidator<UpdateManualHoldingRequest>
{
    public UpdateManualHoldingValidator(TimeProvider clock) =>
        ManualHoldingRules.Apply(this, x => x.Quantity, x => x.AveragePrice, x => x.Location, x => x.Notes,
            x => x.HeldSince, clock);
}

public sealed class RewardValidator : AbstractValidator<RewardRequest>
{
    public RewardValidator(TimeProvider clock)
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Enter how many coins you received.")
            .LessThan(1_000_000_000_000m).WithMessage("That quantity looks too large.");
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.ReceivedOn)
            .Must(d => d is null || d <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1))
            .WithMessage("The date cannot be in the future.");
        RuleFor(x => x.Note).MaximumLength(ManualHolding.MaxNotes);
    }
}

internal static class ManualHoldingRules
{
    public static void Apply<T>(AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, decimal>> quantity,
        System.Linq.Expressions.Expression<Func<T, decimal>> price,
        System.Linq.Expressions.Expression<Func<T, string?>> location,
        System.Linq.Expressions.Expression<Func<T, string?>> notes,
        System.Linq.Expressions.Expression<Func<T, DateOnly?>> heldSince, TimeProvider clock)
    {
        v.RuleFor(quantity).GreaterThan(0).WithMessage("Enter how many coins you hold (more than 0).")
            .LessThan(1_000_000_000_000m).WithMessage("That quantity looks too large.");
        v.RuleFor(price).GreaterThanOrEqualTo(0).WithMessage("The average buy price cannot be negative.")
            .LessThan(100_000_000m).WithMessage("That price looks too large.");
        v.RuleFor(location).MaximumLength(80).WithMessage("Keep the location under 80 characters.");
        v.RuleFor(notes).MaximumLength(ManualHolding.MaxNotes)
            .WithMessage($"Keep notes under {ManualHolding.MaxNotes} characters.");
        v.RuleFor(heldSince)
            .Must(d => d is null || d <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1))
            .WithMessage("The date cannot be in the future.")
            .Must(d => d is null || d >= new DateOnly(2009, 1, 3))
            .WithMessage("The date is before Bitcoin existed.");
    }
}

/// <summary>Coins entered by hand: the only investment records the user writes (no exchange or wallet access).</summary>
public static class ManualHoldingEndpoints
{
    public static IEndpointRouteBuilder MapManualHoldings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/portfolio/manual").WithTags("Portfolio");

        group.MapGet("/coins", (string? q, ManualHoldingService s, CancellationToken ct) => s.SearchAsync(q, ct));
        group.MapGet("/", (ManualHoldingService s, CancellationToken ct) => s.ListAsync(ct));

        group.MapPost("/", async (CreateManualHoldingRequest req, ManualHoldingService s, CancellationToken ct) =>
                (await s.CreateAsync(req, ct)).ToHttp(id => Results.Created($"/api/portfolio/manual/{id}", new { id })))
            .Validate<CreateManualHoldingRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateManualHoldingRequest req, ManualHoldingService s,
                CancellationToken ct) => (await s.UpdateAsync(id, req, ct)).ToHttp())
            .Validate<UpdateManualHoldingRequest>();

        group.MapDelete("/{id:guid}", async (Guid id, ManualHoldingService s, CancellationToken ct) =>
            (await s.DeleteAsync(id, ct)).ToHttp());

        group.MapPost("/{id:guid}/rewards", async (Guid id, RewardRequest req, ManualHoldingService s,
                CancellationToken ct) =>
                (await s.AddRewardAsync(id, req, ct)).ToHttp(rewardId =>
                    Results.Created($"/api/portfolio/manual/{id}/rewards/{rewardId}", new { id = rewardId })))
            .Validate<RewardRequest>();

        group.MapDelete("/{id:guid}/rewards/{rewardId:guid}", async (Guid id, Guid rewardId, ManualHoldingService s,
            CancellationToken ct) => (await s.RemoveRewardAsync(id, rewardId, ct)).ToHttp());

        // Called when the portfolio is opened: prices older than 15 minutes are fetched again.
        group.MapPost("/refresh-prices", async (ManualHoldingService s, CancellationToken ct) =>
            Results.Ok(new { updated = await s.RefreshPricesAsync(ManualHoldingService.PriceMaxAge, ct) }));

        return app;
    }
}
