using System.Text.Json;
using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Accounts;
using Integrations.Application.Connections;
using Integrations.Application.Contracts;
using Integrations.Application.Instruments;
using Integrations.Application.Sync;
using Investments.Application.Portfolio;
using Investments.Application.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Integrations.Application;

public sealed record CredentialField(string Key, string Label, bool Secret, bool Required, string? Hint);

public sealed record ProviderInfo(BrokerKind Kind, string Name, IReadOnlyList<CredentialField> Fields, string SetupHint);

/// <summary>Never contains credentials — only whether they are configured and when they expire.</summary>
public sealed record ConnectionDto(Guid Id, BrokerKind Kind, string DisplayName, Guid AccountId,
    ConnectionStatus Status, string? LastError, DateTimeOffset? LastSuccessfulSyncUtc, DateOnly? CredentialsExpireOn,
    SyncJobDto? LastJob);

public sealed record SyncJobDto(Guid Id, SyncTrigger Trigger, SyncOutcome Outcome, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, int Imported, int Updated, int Ignored, IReadOnlyList<string> Errors);

public sealed record CreateConnectionRequest(BrokerKind Kind, string DisplayName, Dictionary<string, string> Credentials,
    DateOnly? CredentialsExpireOn);

public sealed record UpdateCredentialsRequest(Dictionary<string, string> Credentials, DateOnly? CredentialsExpireOn);

public sealed class CreateConnectionValidator : AbstractValidator<CreateConnectionRequest>
{
    public CreateConnectionValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Credentials).NotNull().Must(c => c.Count <= 10 && c.All(kv => kv.Key.Length <= 40 && kv.Value.Length <= 500));
    }
}

public static class IntegrationEndpoints
{
    public static readonly IReadOnlyList<ProviderInfo> Providers =
    [
        new(BrokerKind.Trading212, "Trading 212",
        [
            new("apiKey", "API key", false, true, null),
            new("apiSecret", "API secret", true, true, "Shown once when the key is created."),
            new("environment", "Environment", false, false, "live (default) or demo"),
        ], "In the app: Settings → API (Beta) → Generate API key. Enable Account data, Metadata, Portfolio and History; leave \"Orders – Execute\" and \"Pies – Write\" off; restrict to your home IP."),
        new(BrokerKind.InteractiveBrokers, "Interactive Brokers",
        [
            new("token", "Flex Web Service token", true, true, null),
            new("queryId", "Activity Flex Query ID", false, true, null),
        ], "Client Portal → Performance & Reports → Flex Queries. Create the Activity query described in docs/broker-integrations.md, enable Flex Web Service, restrict the token to your IP and set its expiry."),
        new(BrokerKind.Binance, "Binance",
        [
            new("apiKey", "API key", false, true, null),
            new("apiSecret", "Secret key", true, true, "Shown once when the key is created."),
        ], "Binance → Account → API Management → Create API (system generated). Leave only \"Enable Reading\" ticked — a key that can trade, transfer or withdraw is refused — and restrict access to your home IP."),
        new(BrokerKind.Demo, "Demo broker (fictitious data)",
        [
            new("profile", "Profile", false, false, "a or b — both hold the same ETF to show consolidation; c has no valuation history, like Trading 212"),
        ], "For demos only. Generates a deterministic, fictitious portfolio."),
    ];

    private static readonly Error NotFound = Error.NotFound("Connection.NotFound", "Connection not found.");

    public static IEndpointRouteBuilder MapIntegrations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/integrations").WithTags("Integrations");

        group.MapGet("/providers", (IInvestmentProviderFactory factory) =>
            Providers.Where(p => factory.Available.Contains(p.Kind)));

        group.MapGet("/connections", async (IIntegrationsDb db, CancellationToken ct) =>
        {
            var connections = await db.Connections.AsNoTracking().OrderBy(c => c.DisplayName).ToListAsync(ct);
            var ids = connections.Select(c => c.Id).ToList();
            var jobs = await db.SyncJobs.AsNoTracking().Where(j => ids.Contains(j.ConnectionId))
                .GroupBy(j => j.ConnectionId)
                .Select(g => g.OrderByDescending(j => j.StartedAtUtc).First())
                .ToListAsync(ct);
            return connections.Select(c => ToDto(c, jobs.FirstOrDefault(j => j.ConnectionId == c.Id)));
        });

        group.MapPost("/connections", async (CreateConnectionRequest req, IIntegrationsDb db, IFinanceDb finance,
            ICredentialProtector protector, IInvestmentProviderFactory factory, SyncQueue queue, TimeProvider clock,
            CancellationToken ct) =>
        {
            if (!factory.Available.Contains(req.Kind))
            {
                return ResultHttp.Problem(Error.Validation("Connection.Kind", "This provider is not enabled."));
            }

            var missing = MissingFields(req.Kind, req.Credentials);
            if (missing is not null)
            {
                return ResultHttp.Problem(missing);
            }

            // Every connection feeds its own read-only broker account in the ledger.
            var account = Account.Create(req.DisplayName, AccountKind.Broker, Currency.Base, 0,
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                Providers.First(p => p.Kind == req.Kind).Name);
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(ct);

            var connection = BrokerConnection.Create(req.Kind, req.DisplayName, account.Id,
                protector.Protect(Clean(req.Credentials)), req.CredentialsExpireOn);
            db.Connections.Add(connection);
            await db.SaveChangesAsync(ct);
            queue.Enqueue(connection.Id);
            return Results.Created($"/api/integrations/connections/{connection.Id}", new { connection.Id });
        }).Validate<CreateConnectionRequest>();

        group.MapPut("/connections/{id:guid}/credentials", async (Guid id, UpdateCredentialsRequest req, IIntegrationsDb db,
            ICredentialProtector protector, CancellationToken ct) =>
        {
            var connection = await db.Connections.FindAsync([id], ct);
            if (connection is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (MissingFields(connection.Kind, req.Credentials) is { } missing)
            {
                return ResultHttp.Problem(missing);
            }

            connection.ReplaceCredentials(protector.Protect(Clean(req.Credentials)), req.CredentialsExpireOn);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/connections/{id:guid}/sync", async (Guid id, IIntegrationsDb db, SyncQueue queue, CancellationToken ct) =>
        {
            var connection = await db.Connections.FindAsync([id], ct);
            if (connection is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            return queue.Enqueue(id)
                ? Results.Accepted()
                : ResultHttp.Problem(Error.Conflict("Sync.Busy", "Too many syncs queued; try again shortly."));
        });

        group.MapPost("/connections/{id:guid}/enable", (Guid id, IIntegrationsDb db, CancellationToken ct) => SetEnabled(id, true, db, ct));
        group.MapPost("/connections/{id:guid}/disable", (Guid id, IIntegrationsDb db, CancellationToken ct) => SetEnabled(id, false, db, ct));

        group.MapGet("/connections/{id:guid}/jobs", async (Guid id, IIntegrationsDb db, CancellationToken ct) =>
            (await db.SyncJobs.AsNoTracking().Where(j => j.ConnectionId == id)
                .OrderByDescending(j => j.StartedAtUtc).Take(50).ToListAsync(ct)).Select(ToDto));

        // Deleting forgets the credentials immediately. Imported history stays unless purge=true.
        group.MapDelete("/connections/{id:guid}", async (Guid id, bool? purge, IIntegrationsDb db, IFinanceDb finance,
            PortfolioSyncWriter writer, TimeProvider clock, CancellationToken ct) =>
        {
            var connection = await db.Connections.FindAsync([id], ct);
            if (connection is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (purge == true)
            {
                await writer.PurgeAccountAsync(connection.AccountId, ct);
            }

            var account = await finance.Accounts.FindAsync([connection.AccountId], ct);
            account?.Archive(clock.GetUtcNow());
            await finance.SaveChangesAsync(ct);
            db.Connections.Remove(connection);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/connections/{id:guid}/csv", ImportCsvAsync).DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");

        app.MapInstruments();
        return app;
    }

    private static async Task<IResult> ImportCsvAsync(Guid id, IFormFile file, IIntegrationsDb db,
        IEnumerable<ICsvHistoryParser> parsers, PortfolioSyncWriter writer, PortfolioSnapshotter snapshotter,
        HistoryRebuildQueue history, TimeProvider clock, CancellationToken ct)
    {
        var connection = await db.Connections.FindAsync([id], ct);
        if (connection is null)
        {
            return ResultHttp.Problem(NotFound);
        }

        var parser = parsers.FirstOrDefault(p => p.Kind == connection.Kind);
        if (parser is null)
        {
            return ResultHttp.Problem(Error.Validation("Csv.Unsupported", "CSV import is not available for this broker."));
        }

        if (file.Length is 0 or > 20 * 1024 * 1024)
        {
            return ResultHttp.Problem(Error.Validation("Csv.File", "Upload a CSV file up to 20 MB."));
        }

        var job = SyncJob.Start(connection.Id, SyncTrigger.CsvImport, clock.GetUtcNow());
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync(ct);
        try
        {
            await using var stream = file.OpenReadStream();
            var data = await parser.ParseAsync(stream, Currency.Base, ct);
            var source = SyncPipeline.ToSource(connection.Kind);
            var counts = await writer.AddTradesAsync(connection.AccountId, source, data.Trades, ct)
                         + await writer.AddCashMovementsAsync(connection.AccountId, source, data.Cash, ct)
                         + await writer.AddDividendsAsync(connection.AccountId, source, data.Dividends, ct);
            await snapshotter.SnapshotTodayAsync(ct);
            history.Enqueue(connection.AccountId);
            job.Finish(data.Warnings.Count == 0 ? SyncOutcome.Succeeded : SyncOutcome.PartiallySucceeded,
                counts.Imported, counts.Updated, counts.Ignored, JsonSerializer.Serialize(data.Warnings.Take(50)),
                clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(job));
        }
        catch (InvalidDataException ex)
        {
            job.Finish(SyncOutcome.Failed, 0, 0, 0, JsonSerializer.Serialize(new[] { ex.Message }), clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return ResultHttp.Problem(Error.Validation("Csv.Invalid", ex.Message));
        }
    }

    private static async Task<IResult> SetEnabled(Guid id, bool enabled, IIntegrationsDb db, CancellationToken ct)
    {
        var connection = await db.Connections.FindAsync([id], ct);
        if (connection is null)
        {
            return ResultHttp.Problem(NotFound);
        }

        connection.SetEnabled(enabled);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Error? MissingFields(BrokerKind kind, Dictionary<string, string> credentials)
    {
        var missing = Providers.First(p => p.Kind == kind).Fields
            .Where(f => f.Required && (!credentials.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.Label).ToList();
        return missing.Count == 0 ? null : Error.Validation("Connection.Credentials", $"Missing: {string.Join(", ", missing)}.");
    }

    private static Dictionary<string, string> Clean(Dictionary<string, string> credentials) =>
        credentials.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

    private static ConnectionDto ToDto(BrokerConnection c, SyncJob? job) => new(c.Id, c.Kind, c.DisplayName, c.AccountId,
        c.Status, c.LastError, c.LastSuccessfulSyncUtc, c.CredentialsExpireOn, job is null ? null : ToDto(job));

    private static SyncJobDto ToDto(SyncJob j) => new(j.Id, j.Trigger, j.Outcome, j.StartedAtUtc, j.FinishedAtUtc,
        j.Imported, j.Updated, j.Ignored, JsonSerializer.Deserialize<List<string>>(j.Errors) ?? []);
}
