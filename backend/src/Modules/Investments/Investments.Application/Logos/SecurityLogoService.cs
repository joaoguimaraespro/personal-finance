using System.Text.RegularExpressions;
using Investments.Application.Abstractions;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Investments.Application.Logos;

/// <summary>Serves a security's logo from the database, fetching it once from the logo source when needed.</summary>
public sealed partial class SecurityLogoService(IInvestmentsDb db, ILogoSource source, TimeProvider clock)
{
    /// <summary>Plain tickers only (AAPL, BRK.B): exchange-suffixed or ISIN-like symbols have no logo there.</summary>
    [GeneratedRegex("^[A-Z]{1,5}(\\.[A-Z])?$")]
    private static partial Regex Ticker();

    public static string? TickerOf(Security s) =>
        s.EffectiveAssetClass is AssetClass.Stock or AssetClass.Etf && Ticker().IsMatch(s.Symbol.ToUpperInvariant())
            ? s.Symbol.ToUpperInvariant()
            : null;

    public async Task<(byte[] Content, string ContentType)?> GetAsync(Guid securityId, CancellationToken ct)
    {
        var logo = await db.SecurityLogos.FirstOrDefaultAsync(l => l.SecurityId == securityId, ct);
        var now = clock.GetUtcNow();
        if (logo is null || (logo.IsStale(now) && source.Enabled))
        {
            var security = await db.Securities.AsNoTracking().FirstOrDefaultAsync(s => s.Id == securityId, ct);
            if (security is null || TickerOf(security) is not { } ticker || !source.Enabled)
            {
                return null;
            }

            (byte[] Content, string ContentType)? fetched;
            try
            {
                fetched = await source.FetchAsync(ticker, ct);
            }
            catch (HttpRequestException)
            {
                return logo?.Content is { } kept ? (kept, logo.ContentType!) : null; // try again another time
            }

            if (logo is null)
            {
                logo = SecurityLogo.Create(securityId);
                db.SecurityLogos.Add(logo);
            }

            logo.Store(fetched?.Content, fetched?.ContentType, now);
            await db.SaveChangesAsync(ct);
        }

        return logo.Content is { } content ? (content, logo.ContentType!) : null;
    }
}
