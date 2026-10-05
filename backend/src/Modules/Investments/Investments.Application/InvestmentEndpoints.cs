using FluentValidation;
using Finance.Application.Http;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Fx;
using Investments.Application.Manual;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;
using Investments.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application;

public sealed record TargetAllocationItem(AssetClass AssetClass, decimal Percent);

public sealed record ManualAssetRequest(string Name, ManualAssetKind Kind, string? Currency, decimal? Value,
    DateOnly? ValuedOn);

public sealed record ValuationRequest(decimal Value, DateOnly? On);

public sealed record ManualAssetDto(Guid Id, string Name, ManualAssetKind Kind, string Currency, bool IsLiability,
    decimal? CurrentValue, DateOnly? ValuedOn, IReadOnlyList<ValuationDto> History);

public sealed record ValuationDto(DateOnly Date, decimal Value);

public sealed class ManualAssetValidator : AbstractValidator<ManualAssetRequest>
{
    public ManualAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Currency).Must(c => c is null || Currency.IsValid(c));
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0).When(x => x.Value is not null);
    }
}

/// <summary>
/// Investment data comes from read-only broker integrations: every endpoint here is GET, except user
/// preferences (target allocation, asset-class overrides), manually valued assets and coins entered by hand.
/// </summary>
public static class InvestmentEndpoints
{
    public static IEndpointRouteBuilder MapInvestments(this IEndpointRouteBuilder app)
    {
        var portfolio = app.MapGroup("/portfolio").WithTags("Portfolio");

        // period: ALL (default), 1M, YTD or 1Y — the return in the summary and its accounts is measured over it.
        portfolio.MapGet("/summary", (PortfolioQueries q, DataSource? broker, Guid? accountId, string? period,
            CancellationToken ct) => q.SummaryAsync(new PortfolioScope(broker, accountId), PeriodReturns.Parse(period), ct));
        portfolio.MapGet("/positions", (PortfolioQueries q, DataSource? broker, Guid? accountId, CancellationToken ct) =>
            q.PositionsAsync(new PortfolioScope(broker, accountId), ct));
        portfolio.MapGet("/allocation", (PortfolioQueries q, DataSource? broker, Guid? accountId, CancellationToken ct) =>
            q.AllocationAsync(new PortfolioScope(broker, accountId), ct));
        portfolio.MapGet("/dividends", (PortfolioQueries q, DataSource? broker, Guid? accountId, DateOnly? from,
            DateOnly? to, CancellationToken ct) => q.DividendsAsync(new PortfolioScope(broker, accountId), from, to, ct));
        // Either a period (measured as the summary's, up to today's live value) or an explicit from/to range.
        portfolio.MapGet("/performance", (PortfolioQueries q, DataSource? broker, Guid? accountId, DateOnly? from,
            DateOnly? to, string? period, CancellationToken ct) =>
            period is not null && from is null && to is null
                ? q.PerformanceAsync(new PortfolioScope(broker, accountId), PeriodReturns.Parse(period), ct)
                : q.PerformanceAsync(new PortfolioScope(broker, accountId), from, to, ct));

        portfolio.MapGet("/targets", async (IInvestmentsDb db, CancellationToken ct) =>
            await db.TargetAllocations.AsNoTracking()
                .Select(t => new TargetAllocationItem(t.AssetClass, t.Percent)).ToListAsync(ct));

        portfolio.MapPut("/targets", async (List<TargetAllocationItem> items, IInvestmentsDb db, CancellationToken ct) =>
        {
            if (items.Any(i => i.Percent is < 0 or > 1) || items.Sum(i => i.Percent) > 1.0001m ||
                items.GroupBy(i => i.AssetClass).Any(g => g.Count() > 1))
            {
                return ResultHttp.Problem(Error.Validation("Targets",
                    "Targets are fractions between 0 and 1, one per asset class, totalling at most 100%."));
            }

            db.TargetAllocations.RemoveRange(await db.TargetAllocations.ToListAsync(ct));
            db.TargetAllocations.AddRange(items.Where(i => i.Percent > 0)
                .Select(i => TargetAllocation.Create(i.AssetClass, i.Percent)));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        portfolio.MapPut("/securities/{id:guid}/asset-class", async (Guid id, AssetClass? assetClass,
            IInvestmentsDb db, CancellationToken ct) =>
        {
            var security = await db.Securities.FindAsync([id], ct);
            if (security is null)
            {
                return ResultHttp.Problem(Error.NotFound("Security.NotFound", "Security not found."));
            }

            security.OverrideAssetClass(assetClass);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var netWorth = app.MapGroup("/net-worth").WithTags("Net worth");
        netWorth.MapGet("/", (NetWorthService s, CancellationToken ct) => s.HistoryAsync(ct));

        var assets = app.MapGroup("/assets").WithTags("Net worth");
        assets.MapGet("/", async (IInvestmentsDb db, CancellationToken ct) =>
        {
            var list = await db.ManualAssets.AsNoTracking().Include(a => a.Valuations)
                .Where(a => a.ArchivedAtUtc == null).OrderBy(a => a.Kind).ThenBy(a => a.Name).ToListAsync(ct);
            return list.Select(ToDto);
        });

        assets.MapPost("/", async (ManualAssetRequest req, IInvestmentsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var asset = ManualAsset.Create(req.Name, req.Kind, req.Currency ?? Currency.Base);
            if (req.Value is { } value)
            {
                asset.Value(req.ValuedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), value);
            }

            db.ManualAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/assets/{asset.Id}", new { asset.Id });
        }).Validate<ManualAssetRequest>();

        assets.MapPost("/{id:guid}/valuations", async (Guid id, ValuationRequest req, IInvestmentsDb db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var asset = await db.ManualAssets.Include(a => a.Valuations).FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null)
            {
                return ResultHttp.Problem(Error.NotFound("Asset.NotFound", "Asset not found."));
            }

            if (req.Value < 0)
            {
                return ResultHttp.Problem(Error.Validation("Asset.Value", "Enter the value as a positive number."));
            }

            asset.Value(req.On ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), req.Value);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        assets.MapPost("/{id:guid}/archive", async (Guid id, IInvestmentsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var asset = await db.ManualAssets.FindAsync([id], ct);
            if (asset is null)
            {
                return ResultHttp.Problem(Error.NotFound("Asset.NotFound", "Asset not found."));
            }

            asset.Archive(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/fx/{currency}", async (string currency, DateOnly? date, FxRates fx, TimeProvider clock,
            CancellationToken ct) =>
        {
            currency = currency.ToUpperInvariant();
            if (!Currency.IsValid(currency))
            {
                return ResultHttp.Problem(Error.Validation("Fx.Currency", "Use an ISO 4217 code."));
            }

            var on = date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var factor = await fx.EurPerUnitAsync(currency, on, ct);
            return factor is null
                ? ResultHttp.Problem(Error.NotFound("Fx.Unavailable", "No reference rate available for that date."))
                : Results.Ok(new { currency, date = on, eurPerUnit = factor });
        }).WithTags("FX");

        app.MapManualHoldings();
        return app;
    }

    private static ManualAssetDto ToDto(ManualAsset a)
    {
        var latest = a.Valuations.OrderByDescending(v => v.Date).FirstOrDefault();
        return new ManualAssetDto(a.Id, a.Name, a.Kind, a.Currency, a.IsLiability, latest?.Value, latest?.Date,
            a.Valuations.OrderBy(v => v.Date).Select(v => new ValuationDto(v.Date, v.Value)).ToList());
    }
}
