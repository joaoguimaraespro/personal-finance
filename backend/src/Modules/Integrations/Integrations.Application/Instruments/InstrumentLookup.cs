using Investments.Domain;

namespace Integrations.Application.Instruments;

/// <summary>Where an autofill suggestion comes from. Crypto is deliberately absent: it is always typed in by hand.</summary>
public enum InstrumentProvider
{
    Trading212 = 0,
    InteractiveBrokers = 1,

    /// <summary>Securities already known locally from broker syncs (works without any API key).</summary>
    Portfolio = 2,
}

/// <param name="BrokerSymbol">The provider's own identifier (T212 ticker, IBKR conid, local security id) used for quotes.</param>
public sealed record InstrumentMatch(InstrumentProvider Provider, string BrokerSymbol, string Symbol, string Name,
    string? Isin, string Currency, AssetClass AssetClass, string? Exchange);

/// <param name="Basis">"trade" (an executed trade that day), "close" (end-of-day price) or "current" (live, today only).</param>
public sealed record InstrumentQuote(InstrumentProvider Provider, DateOnly Date, decimal Price, string Currency,
    string Basis);

public sealed record ProviderState(InstrumentProvider Provider, bool Configured, string? Message);

public sealed record InstrumentSearchResult(IReadOnlyList<InstrumentMatch> Items, IReadOnlyList<ProviderState> Providers);

public sealed record InstrumentQuoteResult(InstrumentQuote? Quote, string? Message);

/// <summary>
/// Read-only lookup of instruments and prices for the add-transaction form. Implementations must never throw for
/// "not configured" — they report <see cref="IsConfigured"/> = false and the form falls back to manual entry.
/// </summary>
public interface IInstrumentSource
{
    InstrumentProvider Provider { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyList<InstrumentMatch>> SearchAsync(string query, int limit, CancellationToken ct);

    Task<InstrumentQuote?> QuoteAsync(string brokerSymbol, DateOnly date, CancellationToken ct);
}

/// <summary>A source is still loading its catalogue (first search after start-up); the client should retry shortly.</summary>
public sealed class InstrumentCatalogLoadingException() : Exception("The instrument list is still loading.");

public static class InstrumentRanking
{
    /// <summary>Exact symbol/ISIN first, then symbol prefix, name prefix, and finally substring matches.</summary>
    public static IReadOnlyList<InstrumentMatch> Rank(IEnumerable<InstrumentMatch> candidates, string query, int limit)
    {
        var q = query.Trim();
        if (q.Length == 0)
        {
            return [];
        }

        static bool Has(string? value, string q) => value?.Contains(q, StringComparison.OrdinalIgnoreCase) == true;

        return candidates
            .Where(c => c.AssetClass != AssetClass.Crypto)
            .Select(c => (Match: c, Score:
                string.Equals(c.Symbol, q, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Isin, q, StringComparison.OrdinalIgnoreCase) ? 0
                : c.Symbol.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1
                : c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 2
                : Has(c.Symbol, q) || Has(c.BrokerSymbol, q) ? 3
                : Has(c.Name, q) ? 4
                : -1))
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score).ThenBy(x => x.Match.Symbol.Length).ThenBy(x => x.Match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Match)
            .ToList();
    }
}
