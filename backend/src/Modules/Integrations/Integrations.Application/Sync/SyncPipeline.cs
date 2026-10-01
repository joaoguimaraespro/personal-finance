using System.Text.Json;
using Integrations.Application.Connections;
using Integrations.Application.Contracts;
using Investments.Application.Portfolio;
using Investments.Application.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Integrations.Application.Sync;

public sealed record SyncResult(Guid JobId, SyncOutcome Outcome, int Imported, int Updated, int Ignored,
    IReadOnlyList<string> Errors);

/// <summary>
/// Fetch → Validate → Normalize → Deduplicate → Persist → Update snapshots. Each run is recorded as a
/// <see cref="SyncJob"/>; invalid records are skipped and reported, never partially written.
/// </summary>
public sealed class SyncPipeline(
    IIntegrationsDb db,
    ICredentialProtector credentials,
    IInvestmentProviderFactory providers,
    PortfolioSyncWriter writer,
    PortfolioSnapshotter snapshotter,
    HistoryRebuildQueue history,
    TimeProvider clock,
    ILogger<SyncPipeline> logger)
{
    /// <summary>Re-read a short window before the last sync: late-booked items are caught, duplicates are ignored.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromDays(10);

    public async Task<SyncResult> RunAsync(Guid connectionId, SyncTrigger trigger, CancellationToken ct)
    {
        var connection = await db.Connections.FirstAsync(c => c.Id == connectionId, ct);
        var job = SyncJob.Start(connection.Id, trigger, clock.GetUtcNow());
        db.SyncJobs.Add(job);
        await db.SaveChangesAsync(ct);

        var errors = new List<string>();
        var counts = SyncCounts.None;
        var source = ToSource(connection.Kind);
        try
        {
            var provider = providers.Create(connection.Kind, credentials.Unprotect(connection.ProtectedCredentials));
            var since = connection.LastSuccessfulSyncUtc - Overlap;

            // Fetch
            var snapshot = await provider.GetAccountSnapshotAsync(ct);
            var positions = await provider.GetPositionsAsync(ct);
            var transactions = await provider.GetTransactionsAsync(since, ct);
            var dividends = await provider.GetDividendsAsync(since, ct);

            // Validate + normalize (invalid rows are reported and skipped)
            var validPositions = Keep(positions, IsValid, p => $"position {p.Security.Symbol}", errors);
            var trades = Keep(transactions.Where(t => t.Trade is not null).Select(t => t.Trade!), IsValid,
                t => $"trade {t.ExternalId}", errors);
            var cash = Keep(transactions.Where(t => t.Cash is not null).Select(t => t.Cash!), IsValid,
                c => $"cash movement {c.ExternalId}", errors);
            var validDividends = Keep(dividends, IsValid, d => $"dividend {d.ExternalId}", errors);

            // Deduplicate + persist (the writer keys everything by source + external id)
            counts += await writer.ReplacePositionsAsync(connection.AccountId, source, validPositions, ct);
            counts += await writer.AddTradesAsync(connection.AccountId, source, trades, ct);
            counts += await writer.AddCashMovementsAsync(connection.AccountId, source, cash, ct);
            counts += await writer.AddDividendsAsync(connection.AccountId, source, validDividends, ct);
            await writer.SetCashAsync(connection.AccountId, snapshot.Currency, snapshot.Cash, ct);
            if (snapshot.History.Count > 0)
            {
                counts += await writer.UpsertSnapshotsAsync(connection.AccountId, snapshot.History,
                    SnapshotOrigins.Broker, ct);
            }

            // Update snapshots
            await snapshotter.SnapshotTodayAsync(ct);
            connection.Succeeded(clock.GetUtcNow());

            // Brokers without valuation history get it rebuilt in the background (prices may need downloading).
            history.Enqueue(connection.AccountId);
        }
        catch (ProviderConfigurationException ex)
        {
            errors.Add(ex.Message);
            connection.NeedsAttention(ex.Message);
        }
        catch (Exception ex) when (ex is ProviderUnavailableException or HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
        {
            errors.Add(ex.Message);
            connection.TemporaryFailure(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected: log the detail server-side, keep the user-facing message generic.
            logger.LogError(ex, "Sync failed for connection {ConnectionId}", connection.Id);
            errors.Add("Unexpected error while synchronising; see server logs.");
            connection.TemporaryFailure("Unexpected error while synchronising.");
        }

        var outcome = connection.LastSuccessfulSyncUtc >= job.StartedAtUtc
            ? errors.Count == 0 ? SyncOutcome.Succeeded : SyncOutcome.PartiallySucceeded
            : SyncOutcome.Failed;
        job.Finish(outcome, counts.Imported, counts.Updated, counts.Ignored,
            JsonSerializer.Serialize(errors.Take(50)), clock.GetUtcNow());
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Sync {Outcome} for {Kind}: {Imported} imported, {Updated} updated, {Ignored} ignored",
            outcome, connection.Kind, counts.Imported, counts.Updated, counts.Ignored);
        return new SyncResult(job.Id, outcome, counts.Imported, counts.Updated, counts.Ignored, errors);
    }

    public static DataSource ToSource(BrokerKind kind) => kind switch
    {
        BrokerKind.Trading212 => DataSource.Trading212,
        BrokerKind.InteractiveBrokers => DataSource.InteractiveBrokers,
        _ => DataSource.Demo,
    };

    private static List<T> Keep<T>(IEnumerable<T> items, Func<T, string?> validate, Func<T, string> describe,
        List<string> errors)
    {
        var kept = new List<T>();
        foreach (var item in items)
        {
            if (validate(item) is { } problem)
            {
                errors.Add($"Skipped {describe(item)}: {problem}");
            }
            else
            {
                kept.Add(item);
            }
        }

        return kept;
    }

    private static readonly DateTimeOffset Earliest = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string? CheckDate(DateTimeOffset at) =>
        at < Earliest || at > clock.GetUtcNow().AddDays(2) ? "date out of range" : null;

    internal static string? CheckSecurity(SecurityReport s) =>
        string.IsNullOrWhiteSpace(s.BrokerSymbol) ? "missing symbol"
        : !Currency.IsValid(s.Currency) ? "invalid currency"
        : s.Isin is { Length: > 0 } isin && isin.Length != 12 ? "invalid ISIN"
        : null;

    private static string? IsValid(PositionReport p) =>
        CheckSecurity(p.Security) ?? (p.Quantity < 0 ? "negative quantity"
            : p.AveragePrice < 0 || p.LastPrice < 0 ? "negative price" : null);

    private string? IsValid(TradeReport t) =>
        CheckSecurity(t.Security) ?? CheckDate(t.ExecutedAt) ?? (t.Quantity <= 0 ? "non-positive quantity"
            : t.Price < 0 ? "negative price"
            : !Currency.IsValid(t.Currency) || !Currency.IsValid(t.AccountCurrency) ? "invalid currency" : null);

    private string? IsValid(CashMovementReport c) =>
        CheckDate(c.OccurredAt) ?? (!Currency.IsValid(c.Currency) ? "invalid currency" : null);

    private string? IsValid(DividendReport d) =>
        CheckSecurity(d.Security) ?? CheckDate(d.PaidOn.ToDateTime(TimeOnly.MinValue))
        ?? (!Currency.IsValid(d.Currency) ? "invalid currency" : null);
}
