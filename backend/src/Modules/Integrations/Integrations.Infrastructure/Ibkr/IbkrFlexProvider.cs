using System.Xml.Linq;
using Integrations.Application.Contracts;
using Investments.Application.Sync;
using Investments.Domain;
using static Integrations.Infrastructure.Ibkr.FlexStatementParser;

namespace Integrations.Infrastructure.Ibkr;

/// <summary>
/// Interactive Brokers via an Activity Flex Query (see docs/broker-integrations.md for the required sections).
/// End-of-day data; history is backfilled in 365-day windows, which is the Flex maximum per request.
/// </summary>
internal sealed class IbkrFlexProvider(FlexClient client, string token, string queryId, int backfillYears,
    TimeProvider clock) : IInvestmentProvider
{
    private List<FlexStatement>? _statements;
    private DateTimeOffset? _loadedSince;

    public BrokerKind Kind => BrokerKind.InteractiveBrokers;

    public async Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct)
    {
        var statements = await LoadAsync(null, ct);
        var latest = statements.OrderBy(s => s.ToDate).Last();
        var flowsByDate = statements.SelectMany(s => s.CashTransactions)
            .Where(c => Attr(c, "type") == "Deposits/Withdrawals")
            .GroupBy(c => DateTime(Attr(c, "dateTime") ?? Attr(c, "reportDate"))?.UtcDateTime.Date)
            .Where(g => g.Key is not null)
            .ToDictionary(g => DateOnly.FromDateTime(g.Key!.Value),
                g => g.Sum(c => (Dec(c, "amount") ?? 0) * (Dec(c, "fxRateToBase") ?? 1)));
        var history = statements.SelectMany(s => s.DailyEquity)
            .Select(e => (Date: Date(Attr(e, "reportDate")), Total: Dec(e, "total"), Cash: Dec(e, "cash")))
            .Where(e => e.Date is not null && e.Total is not null)
            .GroupBy(e => e.Date!.Value).Select(g => g.Last())
            .Select(e => new SnapshotReport(e.Date!.Value, e.Total!.Value, e.Cash ?? 0, latest.BaseCurrency,
                flowsByDate.GetValueOrDefault(e.Date!.Value)))
            .ToList();
        return new AccountSnapshot(latest.BaseCurrency, latest.EndingCash ?? 0,
            latest.EndingValue ?? history.LastOrDefault()?.TotalValue ?? 0, history);
    }

    public async Task<IReadOnlyList<PositionReport>> GetPositionsAsync(CancellationToken ct)
    {
        var latest = (await LoadAsync(null, ct)).OrderBy(s => s.ToDate).Last();
        var asOf = latest.ToDate?.ToDateTime(new TimeOnly(22, 0)) ?? clock.GetUtcNow().UtcDateTime;
        return latest.OpenPositions
            .Where(p => Attr(p, "assetCategory") is not "CASH")
            .Select(p => new PositionReport(Security(p), Dec(p, "position") ?? 0, Dec(p, "costBasisPrice") ?? 0,
                Dec(p, "markPrice") ?? 0, new DateTimeOffset(asOf, TimeSpan.Zero)))
            .ToList();
    }

    public async Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since,
        CancellationToken ct)
    {
        var statements = await LoadAsync(since, ct);
        var result = new List<InvestmentTransaction>();
        foreach (var s in statements)
        {
            foreach (var t in s.Trades.Where(t => Attr(t, "assetCategory") is not "CASH"))
            {
                var id = Attr(t, "tradeID") ?? Attr(t, "transactionID");
                var at = DateTime(Attr(t, "dateTime")) ?? DateTime(Attr(t, "tradeDate"));
                var qty = Dec(t, "quantity") ?? 0;
                if (id is null || at is null || qty == 0)
                {
                    continue;
                }

                var fxToBase = Dec(t, "fxRateToBase") ?? 1m;
                var currency = Attr(t, "currency") ?? s.BaseCurrency;
                result.Add(InvestmentTransaction.Of(new TradeReport($"ibkr:trade:{id}", Security(t),
                    Attr(t, "buySell")?.StartsWith("SELL", StringComparison.Ordinal) == true || qty < 0 ? TradeSide.Sell : TradeSide.Buy,
                    Math.Abs(qty), Dec(t, "tradePrice") ?? 0, currency, Math.Abs(Dec(t, "ibCommission") ?? 0),
                    Math.Abs(Dec(t, "taxes") ?? 0), Attr(t, "ibCommissionCurrency") ?? currency,
                    Dec(t, "netCash") is { } net ? net * fxToBase : null, s.BaseCurrency,
                    Dec(t, "fifoPnlRealized") is { } pnl ? pnl * fxToBase : null, at.Value)));
            }

            foreach (var c in s.CashTransactions)
            {
                var type = Attr(c, "type");
                var amount = Dec(c, "amount") ?? 0;
                var at = DateTime(Attr(c, "dateTime")) ?? DateTime(Attr(c, "reportDate"));
                var id = Attr(c, "transactionID");
                if (at is null || id is null || amount == 0 || type is "Dividends" or "Payment In Lieu Of Dividends" or "Withholding Tax")
                {
                    continue; // dividends and their withholding are reported together by GetDividendsAsync
                }

                var kind = type switch
                {
                    "Deposits/Withdrawals" => amount > 0 ? CashMovementType.Deposit : CashMovementType.Withdrawal,
                    "Broker Interest Received" or "Bond Interest Received" => CashMovementType.Interest,
                    "Broker Interest Paid" or "Other Fees" or "Commission Adjustments" or "Advisor Fees" => CashMovementType.Fee,
                    _ => CashMovementType.Other,
                };
                result.Add(InvestmentTransaction.Of(new CashMovementReport($"ibkr:cash:{id}", kind, amount,
                    Attr(c, "currency") ?? s.BaseCurrency, at.Value, Attr(c, "description"))));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var statements = await LoadAsync(since, ct);
        var items = statements.SelectMany(s => s.CashTransactions).ToList();
        string Key(XElement c) => Attr(c, "actionID") is { } action
            ? $"a:{action}"
            : $"c:{Attr(c, "conid")}:{Date(Attr(c, "dateTime") ?? Attr(c, "reportDate"))}";

        var withholding = items.Where(c => Attr(c, "type") == "Withholding Tax")
            .GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(c => Dec(c, "amount") ?? 0));
        return items.Where(c => Attr(c, "type") is "Dividends" or "Payment In Lieu Of Dividends")
            .GroupBy(Key)
            .Select(g =>
            {
                var first = g.First();
                var gross = g.Sum(c => Dec(c, "amount") ?? 0);
                var wht = Math.Abs(withholding.GetValueOrDefault(g.Key));
                var paidOn = Date(Attr(first, "dateTime") ?? Attr(first, "reportDate")) ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                return new DividendReport($"ibkr:div:{g.Key}", Security(first), paidOn, gross, wht, gross - wht,
                    Attr(first, "currency") ?? "USD", null, null);
            })
            .ToList();
    }

    private async Task<List<FlexStatement>> LoadAsync(DateTimeOffset? since, CancellationToken ct)
    {
        if (_statements is not null && (_loadedSince is null || since is null || since >= _loadedSince))
        {
            return _statements;
        }

        var yesterday = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-1);
        List<FlexStatement> statements;
        if (since is { } s && DateOnly.FromDateTime(s.UtcDateTime) > yesterday.AddDays(-360))
        {
            // Incremental sync: ask for the query's own period (Last 365 Calendar Days) instead of a short, very
            // recent date range — IBKR answers 1003 for ranges ending on a day it hasn't closed yet. Records are
            // keyed by IBKR ids, so re-reading the year is harmless.
            try
            {
                statements = Statements(await client.FetchAsync(token, queryId, null, null, ct)).ToList();
            }
            catch (FlexStatementUnavailableException)
            {
                throw NoStatement();
            }
        }
        else
        {
            statements = await BackfillAsync(since is { } b ? DateOnly.FromDateTime(b.UtcDateTime)
                : yesterday.AddDays(-365 * Math.Clamp(backfillYears, 1, 20) + 1), yesterday, ct);
        }

        if (statements.Count == 0)
        {
            throw new ProviderConfigurationException("The Flex query returned no statements. Check the query's sections.");
        }

        _statements = statements;
        _loadedSince = since;
        return statements;
    }

    /// <summary>
    /// History in 365-day windows (the Flex maximum per request), newest first. When IBKR has no statement for a
    /// window (1003): for the newest one, end it a few days earlier (weekend, or a day IBKR hasn't closed yet);
    /// otherwise the window starts before the account existed — narrow it towards the present until it has a
    /// statement, and don't go further back.
    /// </summary>
    private async Task<List<FlexStatement>> BackfillAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var windows = new List<(DateOnly From, DateOnly To)>();
        for (var from = start; from <= end; from = from.AddDays(365))
        {
            windows.Add((from, from.AddDays(364) < end ? from.AddDays(364) : end));
        }

        var statements = new List<FlexStatement>();
        var unavailable = false;
        foreach (var (from, windowEnd) in Enumerable.Reverse(windows))
        {
            var to = windowEnd;
            var loaded = false;
            var stepsBack = windowEnd == end ? 4 : 0;
            for (var step = 0; step <= stepsBack && !loaded; step++)
            {
                to = windowEnd.AddDays(-step);
                loaded = await TryFetchAsync(from, to, statements, ct);
            }

            var reachedStart = !loaded;
            for (var f = from; !loaded;)
            {
                var span = to.DayNumber - f.DayNumber;
                if (span < 7)
                {
                    break;
                }

                f = f.AddDays(span / 2 + 1);
                loaded = await TryFetchAsync(f, to, statements, ct);
            }

            unavailable |= !loaded;
            if (reachedStart)
            {
                break;
            }
        }

        if (statements.Count == 0 && unavailable)
        {
            throw NoStatement();
        }

        return statements;
    }

    private async Task<bool> TryFetchAsync(DateOnly from, DateOnly to, List<FlexStatement> into, CancellationToken ct)
    {
        try
        {
            into.AddRange(Statements(await client.FetchAsync(token, queryId, from, to, ct)));
            return true;
        }
        catch (FlexStatementUnavailableException)
        {
            return false;
        }
    }

    private static ProviderConfigurationException NoStatement() => new(
        "IBKR has no statement for the requested period (Flex error 1003). Check that the Query ID belongs to " +
        "an Activity Flex Query of this account, with Period \"Last 365 Calendar Days\", and that the account " +
        "already has activity.");

    private static SecurityReport Security(XElement e)
    {
        var conid = Attr(e, "conid") ?? Attr(e, "symbol") ?? "UNKNOWN";
        var category = Attr(e, "assetCategory");
        var sub = Attr(e, "subCategory");
        var assetClass = (category, sub) switch
        {
            (_, "ETF") => AssetClass.Etf,
            ("STK", _) => AssetClass.Stock,
            ("BOND", _) => AssetClass.Bond,
            ("FUND", _) => AssetClass.Fund,
            ("CRYPTO", _) => AssetClass.Crypto,
            _ => AssetClass.Other,
        };
        var isin = Attr(e, "isin");
        return new SecurityReport(conid, isin is { Length: 12 } ? isin : null, Attr(e, "symbol") ?? conid,
            Attr(e, "listingExchange"), Attr(e, "description") ?? Attr(e, "symbol") ?? conid,
            Attr(e, "currency") ?? "USD", assetClass);
    }
}
