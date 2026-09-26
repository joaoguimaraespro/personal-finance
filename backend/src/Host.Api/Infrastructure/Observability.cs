using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Host.Api.Infrastructure;

internal static class Observability
{
    /// <summary>
    /// OpenTelemetry is opt-in: without OTEL_EXPORTER_OTLP_ENDPOINT nothing is exported and the app runs standalone.
    /// Only request shapes, durations and counts are recorded — never amounts, descriptions or query strings.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = false;
            o.IncludeScopes = false;
            o.AddOtlpExporter();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("finance-api"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    // Strip query strings: they may contain search terms or dates the user typed.
                    o.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Microsoft.AspNetCore.Identity")
                .AddOtlpExporter());
        return builder;
    }
}
