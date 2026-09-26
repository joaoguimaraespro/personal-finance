using Integrations.Application.Contracts;
using Investments.Application.Sync;
using Investments.Domain;

namespace Integrations.Infrastructure.Trading212;

/// <summary>Trading 212 via the official Public API v0, read-only. Invest and Stocks ISA accounts.</summary>
internal sealed class Trading212Provider(Trading212Client client) : IInvestmentProvider
{
    private Dictionary<string, T212InstrumentMeta>? _instruments;
    private string _accountCurrency = "EUR";

    public BrokerKind Kind => BrokerKind.Trading212;

    public async Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct)
    {
        var s = await client.SummaryAsync(ct);
        _accountCurrency = s.Currency;
        // T212 exposes no valuation history; the snapshotter records it daily from now on.
        return new AccountSnapshot(s.Currency, s.Cash.AvailableToTrade + s.Cash.ReservedForOrders + s.Cash.InPies,
            s.TotalValue, []);
    }

    public async Task<IReadOnlyList<PositionReport>> GetPositionsAsync(CancellationToken ct)
    {
        var positions = await client.PositionsAsync(ct);
        var meta = await InstrumentsAsync(ct);
        return positions.Select(p => new PositionReport(ToSecurity(p.Instrument, meta), p.Quantity, p.AveragePricePaid,
            p.CurrentPrice, DateTimeOffset.UtcNow)).ToList();
    }

    public async Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since,
        CancellationToken ct)
    {
        var meta = await InstrumentsAsync(ct);
        var orders = await client.HistoryAsync<T212HistoricalOrder>("orders", since,
            o => o.Fill?.FilledAt ?? DateTimeOffset.MaxValue, ct);
        var result = new List<InvestmentTransaction>();
        foreach (var o in orders)
        {
            // Only executed trades. Splits, corporate actions and unfilled orders do not move cash here.
            if (o.Fill is not { } fill || fill.Type is not (null or "TRADE") || fill.Quantity == 0 ||
                o.Order.Side is not ("BUY" or "SELL"))
            {
                continue;
            }

            var instrument = o.Order.Instrument ?? new T212Instrument(o.Order.Ticker, null, o.Order.Ticker, o.Order.Currency);
            var security = ToSecurity(instrument, meta);
            var wallet = fill.WalletImpact;
            var walletCurrency = wallet?.Currency ?? _accountCurrency;
            var taxes = (wallet?.Taxes ?? []).Where(t => (t.Currency ?? walletCurrency) == walletCurrency).ToList();
            static bool IsTax(T212Tax t) => t.Name is { } n && (n.Contains("TAX", StringComparison.Ordinal) ||
                                                                n.Contains("STAMP_DUTY", StringComparison.Ordinal) ||
                                                                n.Contains("LEVY", StringComparison.Ordinal));
            var side = o.Order.Side == "BUY" ? TradeSide.Buy : TradeSide.Sell;
            var net = wallet?.NetValue is { } nv ? (side == TradeSide.Buy ? -Math.Abs(nv) : Math.Abs(nv)) : (decimal?)null;
            result.Add(InvestmentTransaction.Of(new TradeReport(
                Trading212Keys.ForTrade(security.Isin, instrument.Ticker, fill.FilledAt, Math.Abs(fill.Quantity)),
                security, side, Math.Abs(fill.Quantity), fill.Price, o.Order.Currency ?? security.Currency,
                taxes.Where(t => !IsTax(t)).Sum(t => Math.Abs(t.Quantity)), taxes.Where(IsTax).Sum(t => Math.Abs(t.Quantity)),
                walletCurrency, net, walletCurrency, wallet?.RealisedProfitLoss, fill.FilledAt)));
        }

        var movements = await client.HistoryAsync<T212Transaction>("transactions", since, t => t.DateTime, ct);
        foreach (var t in movements)
        {
            var (type, amount) = t.Type switch
            {
                "DEPOSIT" => (CashMovementType.Deposit, Math.Abs(t.Amount)),
                "WITHDRAW" => (CashMovementType.Withdrawal, -Math.Abs(t.Amount)),
                "FEE" => (CashMovementType.Fee, -Math.Abs(t.Amount)),
                "INTEREST_ON_FREE_CASH" or "LENDING_INTEREST" => (CashMovementType.Interest, Math.Abs(t.Amount)),
                _ => (CashMovementType.Other, t.Amount),
            };
            result.Add(InvestmentTransaction.Of(new CashMovementReport(Trading212Keys.ForCash(type.ToString(), t.DateTime, amount),
                type, amount, t.Currency ?? _accountCurrency, t.DateTime, t.Type)));
        }

        return result;
    }

    public async Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var meta = await InstrumentsAsync(ct);
        var dividends = await client.HistoryAsync<T212Dividend>("dividends", since, d => d.PaidOn, ct);
        return dividends.Select(d =>
        {
            var instrument = d.Instrument ?? new T212Instrument(d.Ticker, null, d.Ticker, d.TickerCurrency);
            var security = ToSecurity(instrument, meta);
            var currency = d.Currency ?? _accountCurrency;
            var paidOn = DateOnly.FromDateTime(d.PaidOn.UtcDateTime);
            // Gross per share is in the instrument's currency; only comparable with the net amount if they match.
            var comparable = (d.TickerCurrency ?? security.Currency) == currency;
            return new DividendReport(Trading212Keys.ForDividend(security.Isin, instrument.Ticker, paidOn, d.Amount),
                security, paidOn, null, null, d.Amount, currency, comparable ? d.GrossAmountPerShare : null,
                comparable ? d.Quantity : null);
        }).ToList();
    }

    private async Task<Dictionary<string, T212InstrumentMeta>> InstrumentsAsync(CancellationToken ct) =>
        _instruments ??= (await client.InstrumentsAsync(ct))
            .GroupBy(i => i.Ticker).ToDictionary(g => g.Key, g => g.First());

    internal static SecurityReport ToSecurity(T212Instrument instrument, IReadOnlyDictionary<string, T212InstrumentMeta> meta)
    {
        var ticker = instrument.Ticker ?? instrument.Isin ?? "UNKNOWN";
        meta.TryGetValue(ticker, out var m);
        var symbol = m?.ShortName ?? ticker.Split('_')[0];
        return new SecurityReport(ticker, instrument.Isin ?? m?.Isin, symbol, null,
            instrument.Name ?? m?.Name ?? symbol, instrument.Currency ?? m?.CurrencyCode ?? "EUR", AssetClassOf(m?.Type));
    }

    internal static AssetClass AssetClassOf(string? type) => type?.ToUpperInvariant() switch
    {
        "ETF" => AssetClass.Etf,
        "STOCK" => AssetClass.Stock,
        "FUND" or "MUTUAL_FUND" => AssetClass.Fund,
        "CRYPTO" or "CRYPTOCURRENCY" => AssetClass.Crypto,
        "BOND" => AssetClass.Bond,
        null => AssetClass.Stock,
        _ => AssetClass.Other,
    };
}
