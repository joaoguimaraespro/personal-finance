using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Integrations.Application.Instruments;

/// <summary>
/// Securities already synced from connected brokers, with the prices those syncs recorded (daily closes and
/// executed trades). Needs no API key and never leaves the database.
/// </summary>
public sealed class PortfolioInstrumentSource(IInvestmentsDb db) : IInstrumentSource
{
    /// <summary>How far back a recorded close may be used for a date without its own price (weekends, holidays).</summary>
    private const int MaxCloseAgeDays = 7;

    public InstrumentProvider Provider => InstrumentProvider.Portfolio;

    public bool IsConfigured => true;

    public async Task<IReadOnlyList<InstrumentMatch>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var term = query.Trim().ToLower();
        var securities = await db.Securities.AsNoTracking()
            .Where(s => s.Symbol.ToLower().Contains(term) || s.Name.ToLower().Contains(term) ||
                        (s.Isin != null && s.Isin.ToLower() == term))
            .OrderBy(s => s.Symbol)
            .Take(200)
            .ToListAsync(ct);
        return InstrumentRanking.Rank(securities.Select(s => new InstrumentMatch(InstrumentProvider.Portfolio,
            s.Id.ToString(), s.Symbol, s.Name, s.Isin, s.Currency, s.EffectiveAssetClass, s.Exchange)), query, limit);
    }

    public async Task<InstrumentQuote?> QuoteAsync(string brokerSymbol, DateOnly date, CancellationToken ct) =>
        Guid.TryParse(brokerSymbol, out var id) ? await QuoteAsync(id, date, ct) : null;

    /// <summary>Price of a locally known security, found by ISIN (preferred) or symbol.</summary>
    public async Task<InstrumentQuote?> QuoteByIdentityAsync(string? isin, string? symbol, DateOnly date,
        CancellationToken ct)
    {
        var normalisedIsin = isin?.Trim().ToUpperInvariant();
        var normalisedSymbol = symbol?.Trim().ToUpper();
        var security = !string.IsNullOrEmpty(normalisedIsin)
            ? await db.Securities.AsNoTracking().FirstOrDefaultAsync(s => s.Isin == normalisedIsin, ct)
            : !string.IsNullOrEmpty(normalisedSymbol)
                ? await db.Securities.AsNoTracking().FirstOrDefaultAsync(s => s.Symbol.ToUpper() == normalisedSymbol, ct)
                : null;
        return security is null || security.EffectiveAssetClass == AssetClass.Crypto
            ? null
            : await QuoteAsync(security.Id, date, ct);
    }

    private async Task<InstrumentQuote?> QuoteAsync(Guid securityId, DateOnly date, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var trades = await db.Trades.AsNoTracking()
            .Where(t => t.SecurityId == securityId && t.ExecutedAtUtc >= dayStart && t.ExecutedAtUtc < dayEnd)
            .Select(t => new { t.Quantity, t.Price, t.Currency })
            .ToListAsync(ct);
        if (trades.Count > 0 && trades.Sum(t => t.Quantity) > 0 && trades.All(t => t.Currency == trades[0].Currency))
        {
            // Quantity-weighted average of the fills that day.
            var price = trades.Sum(t => t.Quantity * t.Price) / trades.Sum(t => t.Quantity);
            return new InstrumentQuote(Provider, date, decimal.Round(price, 6), trades[0].Currency, "trade");
        }

        var oldest = date.AddDays(-MaxCloseAgeDays);
        var close = await db.MarketPrices.AsNoTracking()
            .Where(p => p.SecurityId == securityId && p.Date <= date && p.Date >= oldest)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);
        return close is null ? null : new InstrumentQuote(Provider, close.Date, close.Close, close.Currency, "close");
    }
}
