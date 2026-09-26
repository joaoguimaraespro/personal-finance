using Finance.Application.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace Exports.Application;

public static class ExportEndpoints
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IServiceCollection AddExports(this IServiceCollection services) => services
        .AddScoped<ExportData>().AddScoped<WorkbookBuilder>().AddScoped<CsvExport>().AddScoped<JsonArchive>();

    public static IEndpointRouteBuilder MapExports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/exports").WithTags("Exports");

        group.MapGet("/xlsx", async (ExportKind kind, string? period, int? year, DateOnly? from, DateOnly? to,
            WorkbookBuilder builder, TimeProvider clock, CancellationToken ct) =>
        {
            var range = Range(kind, period, year, from, to, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            if (range.IsFailure)
            {
                return ResultHttp.Problem(range.Error);
            }

            var bytes = await builder.BuildAsync(new ExportRequest(kind, range.Value.From, range.Value.To), ct);
            return Results.File(bytes, XlsxType, $"personal-finance-{kind.ToString().ToLowerInvariant()}-{range.Value.From:yyyyMMdd}-{range.Value.To:yyyyMMdd}.xlsx");
        });

        group.MapGet("/csv", async (CsvDataset dataset, DateOnly? from, DateOnly? to, string? dialect, CsvExport csv,
            TimeProvider clock, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var start = from ?? new DateOnly(1970, 1, 1);
            var end = to ?? today;
            var bytes = await csv.BuildAsync(dataset, start, end, dialect == "excel-pt", ct);
            return Results.File(bytes, "text/csv; charset=utf-8", $"personal-finance-{dataset.ToString().ToLowerInvariant()}.csv");
        });

        group.MapGet("/json", async (JsonArchive archive, TimeProvider clock, CancellationToken ct) =>
            Results.File(await archive.ExportAsync(ct), "application/json",
                $"personal-finance-full-{clock.GetUtcNow():yyyyMMdd-HHmm}.json"));

        app.MapPost("/imports/json", async (IFormFile file, JsonArchive archive, CancellationToken ct) =>
        {
            if (file.Length is 0 or > 100 * 1024 * 1024)
            {
                return ResultHttp.Problem(Error.Validation("Json.File", "Upload a JSON export up to 100 MB."));
            }

            await using var stream = file.OpenReadStream();
            var result = await archive.ImportAsync(stream, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ResultHttp.Problem(result.Error);
        }).WithTags("Imports").DisableAntiforgery().Accepts<IFormFile>("multipart/form-data");

        return app;
    }

    private static Result<(DateOnly From, DateOnly To)> Range(ExportKind kind, string? period, int? year, DateOnly? from,
        DateOnly? to, DateOnly today)
    {
        switch (kind)
        {
            case ExportKind.Month:
                if (!YearMonth.TryParse(period ?? YearMonth.From(today).ToString(), out var ym))
                {
                    return Error.Validation("Export.Period", "Use period=yyyy-MM.");
                }

                return (ym.FirstDay, ym.LastDay);
            case ExportKind.Year:
                var y = year ?? today.Year;
                return (new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
            case ExportKind.Period:
                if (from is null || to is null || to < from)
                {
                    return Error.Validation("Export.Range", "Give from and to (yyyy-MM-dd), from ≤ to.");
                }

                return (from.Value, to.Value);
            default:
                return (from ?? new DateOnly(1970, 1, 1), to ?? today);
        }
    }
}
