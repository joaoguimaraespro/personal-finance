using Integrations.Application.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Integrations.Application.Instruments;

/// <summary>
/// Search-as-you-type and price-on-date for the add-transaction form. Every failure (no key configured, broker down,
/// rate limited, catalogue still loading) degrades to "no suggestion" plus a message; the form stays usable by hand.
/// </summary>
public static class InstrumentEndpoints
{
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(8);

    public static IEndpointRouteBuilder MapInstruments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/instruments").WithTags("Instruments");

        group.MapGet("/search", async (string? q, int? limit, [FromServices] IEnumerable<IInstrumentSource> brokers,
            [FromServices] PortfolioInstrumentSource portfolio, [FromServices] ILoggerFactory logs,
            CancellationToken ct) =>
        {
            var query = (q ?? "").Trim();
            var take = Math.Clamp(limit ?? 12, 1, 25);
            IInstrumentSource[] sources = [.. brokers, portfolio];
            if (query.Length is 0 or > 64)
            {
                return Results.Ok(new InstrumentSearchResult([],
                    sources.Select(s => new ProviderState(s.Provider, s.IsConfigured, null)).ToList()));
            }

            var log = logs.CreateLogger(typeof(InstrumentEndpoints));
            var items = new List<InstrumentMatch>();
            var states = new List<ProviderState>();
            // Sequential on purpose: the portfolio source shares the request's DbContext.
            foreach (var source in sources)
            {
                if (!source.IsConfigured)
                {
                    states.Add(new ProviderState(source.Provider, false, null));
                    continue;
                }

                var (matches, message) = await TryAsync(source, token => source.SearchAsync(query, take, token), log, ct);
                if (matches is not null)
                {
                    items.AddRange(matches);
                }

                states.Add(new ProviderState(source.Provider, true, message));
            }

            // One suggestion per instrument: the first provider to report an ISIN wins.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = items.Where(i => i.Isin is null || seen.Add(i.Isin)).Take(take * 2).ToList();
            return Results.Ok(new InstrumentSearchResult(unique, states));
        });

        group.MapGet("/quote", async (InstrumentProvider provider, string symbol, DateOnly date, string? isin,
            [FromServices] IEnumerable<IInstrumentSource> brokers, [FromServices] PortfolioInstrumentSource portfolio,
            [FromServices] ILoggerFactory logs, CancellationToken ct) =>
        {
            var log = logs.CreateLogger(typeof(InstrumentEndpoints));
            var source = provider == InstrumentProvider.Portfolio
                ? portfolio
                : brokers.FirstOrDefault(b => b.Provider == provider);

            string? message = null;
            if (source is { IsConfigured: true } configured)
            {
                var (quote, error) = await TryAsync(configured,
                    token => configured.QuoteAsync(symbol, date, token), log, ct);
                if (quote is not null)
                {
                    return Results.Ok(new InstrumentQuoteResult(quote, null));
                }

                message = error;
            }

            // Brokers rarely expose historical prices; fall back to what earlier syncs recorded for this instrument.
            if (provider != InstrumentProvider.Portfolio && !string.IsNullOrWhiteSpace(isin))
            {
                var (local, _) = await TryAsync(portfolio,
                    token => portfolio.QuoteByIdentityAsync(isin, null, date, token), log, ct);
                if (local is not null)
                {
                    return Results.Ok(new InstrumentQuoteResult(local, null));
                }
            }

            return Results.Ok(new InstrumentQuoteResult(null,
                message ?? "No price available for this date; enter it manually."));
        });

        return app;
    }

    private static async Task<(T? Value, string? Message)> TryAsync<T>(IInstrumentSource source,
        Func<CancellationToken, Task<T>> call, ILogger log, CancellationToken requestAborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(SourceTimeout);
        try
        {
            return (await call(timeout.Token), null);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InstrumentCatalogLoadingException or TimeoutException
                                       or OperationCanceledException)
        {
            return (default, "Still loading the instrument list; try again in a few seconds.");
        }
        catch (ProviderConfigurationException ex)
        {
            return (default, ex.Message);
        }
        catch (Exception ex)
        {
            // Never break the form because a broker is down; details go to the server log (never credentials).
            log.LogWarning(ex, "Instrument lookup via {Provider} failed", source.Provider);
            return (default, $"{source.Provider} is unavailable right now; enter the details manually.");
        }
    }
}
