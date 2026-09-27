using Integrations.Application.Instruments;
using Integrations.Infrastructure.Instruments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Integrations.Infrastructure.Trading212;

/// <summary>
/// Instrument search over Trading 212's full tradable-instrument list (official Public API v0,
/// <c>/equity/metadata/instruments</c>), keyed from environment configuration (<c>MarketData__Trading212__*</c>).
/// The API has no search endpoint and no historical prices: the list (limited to 1 call / 50 s) is cached and
/// searched locally, and a price is only available for instruments currently held, for today's date.
/// </summary>
internal sealed class Trading212InstrumentSource(IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock)
    : IInstrumentSource
{
    private readonly CatalogCache<List<T212InstrumentMeta>> _instruments = new(TimeSpan.FromHours(12), clock);
    private readonly CatalogCache<List<T212Position>> _positions = new(TimeSpan.FromMinutes(5), clock);

    private string? ApiKey => config["MarketData:Trading212:ApiKey"];
    private string? ApiSecret => config["MarketData:Trading212:ApiSecret"];
    private bool Demo => string.Equals(config["MarketData:Trading212:Environment"], "demo", StringComparison.OrdinalIgnoreCase);

    public InstrumentProvider Provider => InstrumentProvider.Trading212;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);

    public async Task<IReadOnlyList<InstrumentMatch>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var instruments = await _instruments.GetAsync(() => LoadAsync(c => c.InstrumentsAsync(CancellationToken.None)))
            .WaitAsync(ct);
        return InstrumentRanking.Rank(instruments.Select(ToMatch), query, limit);
    }

    public async Task<InstrumentQuote?> QuoteAsync(string brokerSymbol, DateOnly date, CancellationToken ct)
    {
        // Only a live price exists (and only for held positions), so it is offered for today's date only.
        if (date != DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        var positions = await _positions.GetAsync(() => LoadAsync(c => c.PositionsAsync(CancellationToken.None)))
            .WaitAsync(ct);
        var held = positions.Find(p => p.Instrument?.Ticker == brokerSymbol);
        return held is null || held.CurrentPrice <= 0
            ? null
            : new InstrumentQuote(Provider, date, held.CurrentPrice, held.Instrument?.Currency ?? "EUR", "current");
    }

    private async Task<T> LoadAsync<T>(Func<Trading212Client, Task<T>> read)
    {
        using var scope = scopes.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<Trading212Client>().Configure(ApiKey!, ApiSecret!, Demo);
        return await read(client);
    }

    private static InstrumentMatch ToMatch(T212InstrumentMeta m) => new(InstrumentProvider.Trading212, m.Ticker,
        m.ShortName ?? m.Ticker.Split('_')[0], m.Name ?? m.ShortName ?? m.Ticker, m.Isin, m.CurrencyCode ?? "EUR",
        Trading212Provider.AssetClassOf(m.Type), null);
}
