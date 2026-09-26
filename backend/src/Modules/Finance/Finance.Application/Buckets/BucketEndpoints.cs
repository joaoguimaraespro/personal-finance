using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Allocation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Buckets;

public sealed record BucketDto(Guid Id, string Key, string Name, BucketGroup Group, bool IsSystem, bool Archived);

public sealed record CreateBucketRequest(string Name, BucketGroup Group);

public sealed record RenameBucketRequest(string Name);

public sealed record AllocationCheckDto(Guid BucketId, AllocationStatus Status);

public sealed record SetAllocationCheckRequest(Guid BucketId, AllocationStatus Status);

public sealed class CreateBucketValidator : AbstractValidator<CreateBucketRequest>
{
    public CreateBucketValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Group).IsInEnum();
    }
}

public static class BucketEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Bucket.NotFound", "Allocation bucket not found.");

    public static void MapBuckets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/buckets").WithTags("Allocation");

        group.MapGet("/", async (IFinanceDb db, CancellationToken ct) =>
            Results.Ok(await db.Buckets.AsNoTracking()
                .OrderBy(b => b.Group).ThenBy(b => b.SortOrder).ThenBy(b => b.Name)
                .Select(b => new BucketDto(b.Id, b.Key, b.Name, b.Group, b.IsSystem, b.ArchivedAtUtc != null))
                .ToListAsync(ct)));

        group.MapPost("/", async (CreateBucketRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var bucket = AllocationBucket.CreateCustom(req.Name, req.Group);
            db.Buckets.Add(bucket);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/buckets/{bucket.Id}", new { bucket.Id });
        }).Validate<CreateBucketRequest>();

        group.MapPut("/{id:guid}", async (Guid id, RenameBucketRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var bucket = await db.Buckets.FindAsync([id], ct);
            if (bucket is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 60)
            {
                return ResultHttp.Problem(Error.Validation("Bucket.Name", "Name is required (max 60)."));
            }

            bucket.Rename(req.Name);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var bucket = await db.Buckets.FindAsync([id], ct);
            if (bucket is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            bucket.Archive(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var checks = app.MapGroup("/allocation-checks").WithTags("Allocation");

        checks.MapGet("/{period}", async (string period, IFinanceDb db, CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(Error.Validation("Period", "Use yyyy-MM."));
            }

            return Results.Ok(await db.AllocationChecks.AsNoTracking()
                .Where(c => c.Year == ym.Year && c.Month == ym.Month)
                .Select(c => new AllocationCheckDto(c.BucketId, c.Status))
                .ToListAsync(ct));
        });

        checks.MapPut("/{period}", async (string period, SetAllocationCheckRequest req, IFinanceDb db,
            CancellationToken ct) =>
        {
            if (!YearMonth.TryParse(period, out var ym))
            {
                return ResultHttp.Problem(Error.Validation("Period", "Use yyyy-MM."));
            }

            if (!await db.Buckets.AnyAsync(b => b.Id == req.BucketId, ct))
            {
                return ResultHttp.Problem(NotFound);
            }

            var check = await db.AllocationChecks.FirstOrDefaultAsync(
                c => c.Year == ym.Year && c.Month == ym.Month && c.BucketId == req.BucketId, ct);
            if (check is null)
            {
                db.AllocationChecks.Add(AllocationCheck.Create(ym, req.BucketId, req.Status));
            }
            else
            {
                check.Set(req.Status);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
