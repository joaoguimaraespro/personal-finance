using Integrations.Application.Instruments;
using Integrations.Infrastructure.Instruments;
using Investments.Application.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Integrations.Infrastructure.Ibkr;

/// <summary>
/// Instrument lookup through the IBKR Flex Web Service, keyed from environment configuration
/// (<c>MarketData__Ibkr__Token</c>, <c>MarketData__Ibkr__QueryId</c>). Flex is a reporting interface with no
/// instrument search and no market data, so the closest honest equivalent is used: the last 365 days of the
/// account's statement (open positions and executed trades) is cached and searched, the price on a date is the
/// account's own fill price that day, and otherwise the latest end-of-day mark price.
/// </summary>
internal sealed class IbkrInstrumentSource(IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock)
    : IInstrumentSource
{
    private readonly CatalogCache<Catalog> _catalog = new(TimeSpan.FromHours(6), clock);

    private string? Token => config["MarketData:Ibkr:Token"];
    private string? QueryId => config["MarketData:Ibkr:QueryId"];

    public InstrumentProvider Provider => InstrumentProvider.InteractiveBrokers;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(QueryId);

    public async Task<IReadOnlyList<InstrumentMatch>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var catalog = await _catalog.GetAsync(LoadAsync).WaitAsync(ct);
        var securities = catalog.Positions.Select(p => p.Security)
            .Concat(catalog.Trades.Select(t => t.Security))
            .DistinctBy(s => s.BrokerSymbol)
            .Select(s => new InstrumentMatch(Provider, s.BrokerSymbol, s.Symbol, s.Name, s.Isin, s.Currency,
                s.AssetClass, s.Exchange));
        return InstrumentRanking.Rank(securities, query, limit);
    }

    public async Task<InstrumentQuote?> QuoteAsync(string brokerSymbol, DateOnly date, CancellationToken ct)
    {
        var catalog = await _catalog.GetAsync(LoadAsync).WaitAsync(ct);
        var fills = catalog.Trades
            .Where(t => t.Security.BrokerSymbol == brokerSymbol &&
                        DateOnly.FromDateTime(t.ExecutedAt.UtcDateTime) == date)
            .ToList();
        var quantity = fills.Sum(t => t.Quantity);
        if (quantity > 0 && fills.TrueForAll(t => t.Currency == fills[0].Currency))
        {
            var price = fills.Sum(t => t.Quantity * t.Price) / quantity;
            return new InstrumentQuote(Provider, date, decimal.Round(price, 6), fills[0].Currency, "trade");
        }

        // Mark prices are end-of-day as of the statement date; use them for that date or later only.
        var position = catalog.Positions.Find(p => p.Security.BrokerSymbol == brokerSymbol);
        if (position is null || position.LastPrice <= 0)
        {
            return null;
        }

        var asOf = DateOnly.FromDateTime(position.PriceAsOf.UtcDateTime);
        return date >= asOf
            ? new InstrumentQuote(Provider, asOf, position.LastPrice, position.Security.Currency, "close")
            : null;
    }

    private async Task<Catalog> LoadAsync()
    {
        using var scope = scopes.CreateScope();
        var provider = new IbkrFlexProvider(scope.ServiceProvider.GetRequiredService<FlexClient>(), Token!, QueryId!,
            backfillYears: 1, clock);
        var positions = await provider.GetPositionsAsync(CancellationToken.None);
        // Same statements as above (already loaded): trades only, cash movements are irrelevant here.
        var trades = (await provider.GetTransactionsAsync(null, CancellationToken.None))
            .Where(t => t.Trade is not null)
            .Select(t => t.Trade!)
            .ToList();
        return new Catalog([.. positions], trades);
    }

    private sealed record Catalog(List<PositionReport> Positions, List<TradeReport> Trades);
}
