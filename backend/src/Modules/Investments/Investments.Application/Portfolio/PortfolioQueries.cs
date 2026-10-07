using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Fx;
using Investments.Application.Sync;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Portfolio;

/// <summary>Which slice of the portfolio to look at: everything, one broker, or one account.</summary>
public sealed record PortfolioScope(DataSource? Broker = null, Guid? AccountId = null);

public sealed record Holding(
    Guid AccountId,
    string AccountName,
    DataSource Broker,
    Guid SecurityId,
    string Symbol,
    string? Isin,
    string Name,
    string Currency,
    AssetClass AssetClass,
    decimal Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal MarketValueBase,
    decimal CostBase,
    DateTimeOffset PriceAsOfUtc,
    decimal? DayChangeBase,
    DayChangeBasis DayChangeBasis = DayChangeBasis.PreviousClose);

public sealed record PositionLine(
    Guid SecurityId,
    string Symbol,
    string? Isin,
    string Name,
    string Currency,
    AssetClass AssetClass,
    decimal Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal MarketValueBase,
    decimal CostBase,
    decimal UnrealizedPnlBase,
    decimal? UnrealizedPnlPercent,
    decimal PortfolioWeight,
    IReadOnlyList<PositionHolding> Holdings,
    decimal? DayChangeBase,
    decimal? DayChangePercent,
    DayChangeBasis DayChangeBasis = DayChangeBasis.PreviousClose);

public sealed record PositionHolding(Guid AccountId, string AccountName, DataSource Broker, decimal Quantity,
    decimal AveragePrice);

public sealed record PortfolioSummary(
    decimal TotalValue,
    decimal MarketValue,
    decimal Cash,
    decimal NetContributions,
    decimal TotalReturn,
    decimal? TotalReturnPercent,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal Dividends,
    decimal Fees,
    int Positions,
    DateTimeOffset? LastSyncUtc,
    IReadOnlyList<AccountTotal> Accounts,
    decimal? DayChange,
    decimal? DayChangePercent,
    DateOnly? Since = null,
    PeriodReturn? PeriodReturn = null,
    DayChangeBasis DayChangeBasis = DayChangeBasis.PreviousClose);

/// <summary>
/// The return over the period asked for. For "ALL" it is the total return (value minus net contributions, over net
/// contributions) since <see cref="From"/>, the first deposit, trade or valuation. For 1M, YTD and 1Y,
/// <see cref="Percent"/> is the time-weighted return (deposits and withdrawals don't distort it) and
/// <see cref="Gain"/> is value at the end − value at the start − net deposits in the period, up to the live value.
/// </summary>
/// <param name="Period">"ALL", "1M", "YTD" or "1Y".</param>
/// <param name="From">The day the return is measured from: the period's start (the last value on or before it),
/// or the first value when history begins later (<see cref="Partial"/>).</param>
/// <param name="Partial">History starts inside the period, so the return covers less than the period.</param>
/// <param name="TimeWeighted">True when <see cref="Percent"/> is a time-weighted return.</param>
public sealed record PeriodReturn(string Period, decimal? Gain, decimal? Percent, DateOnly? From, bool Partial,
    bool TimeWeighted);

/// <summary>One account ("wallet") of the summary, with enough to draw its card without a scoped request.</summary>
/// <param name="TotalReturn">Value minus net contributions of this account, as the summary computes it.</param>
/// <param name="DayChange">Change since the previous close of the account's holdings (coins: over the last 24 hours,
/// see <see cref="DayChangeBasis"/>); null while unknown.</param>
/// <param name="Since">First deposit, trade or valuation of the account.</param>
public sealed record AccountTotal(
    Guid AccountId,
    string Name,
    DataSource Broker,
    decimal MarketValue,
    decimal Cash,
    decimal NetContributions = 0,
    decimal TotalReturn = 0,
    decimal? TotalReturnPercent = null,
    decimal? DayChange = null,
    decimal? DayChangePercent = null,
    int Positions = 0,
    DateOnly? Since = null,
    PeriodReturn? PeriodReturn = null,
    DayChangeBasis DayChangeBasis = DayChangeBasis.PreviousClose);

public sealed record AllocationLine(AssetClass AssetClass, decimal Value, decimal Actual, decimal? Target,
    decimal? Difference);

public sealed record DividendLine(DateOnly PaidOn, string Symbol, string Name, DataSource Broker, decimal Gross,
    decimal WithholdingTax, decimal Net, string Currency, decimal NetBase, bool WithholdingDerived);

public sealed record DividendSummary(decimal TotalNetBase, IReadOnlyList<PeriodAmount> ByMonth,
    IReadOnlyList<SecurityAmount> BySecurity, IReadOnlyList<DividendLine> Items);

public sealed record PeriodAmount(string Period, decimal Amount);

public sealed record SecurityAmount(string Symbol, string Name, decimal Amount);

/// <param name="ReconstructedBefore">History before this day was rebuilt from transactions and public closing
/// prices (null when none was).</param>
/// <param name="EstimatedDays">Reconstructed days on which some holding was valued at a trade price.</param>
public sealed record PerformanceReport(
    DateOnly From,
    DateOnly To,
    decimal? TimeWeightedReturn,
    decimal? MoneyWeightedReturn,
    decimal StartValue,
    decimal EndValue,
    decimal NetFlows,
    decimal Gain,
    IReadOnlyList<ValuePoint> Series,
    DateOnly? ReconstructedBefore = null,
    int EstimatedDays = 0);

public sealed record ValuePoint(DateOnly Date, decimal Value, decimal NetContributions);

public sealed class PortfolioQueries(IInvestmentsDb db, IFinanceDb finance, FxRates fx, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<Holding>> HoldingsAsync(PortfolioScope scope, CancellationToken ct)
    {
        var accounts = await BrokerAccountsAsync(scope, ct);
        var ids = accounts.Keys.ToList();
        var rows = await (
                from p in db.Positions.AsNoTracking()
                join s in db.Securities.AsNoTracking() on p.SecurityId equals s.Id
                where ids.Contains(p.AccountId)
                select new { p, s })
            .ToListAsync(ct);
        var factors = await fx.EurPerUnitAsync(rows.Select(r => r.s.Currency), Today, ct);
        var securityIds = rows.Select(r => r.s.Id).Distinct().ToList();
        var since = Today.AddDays(-14);
        var closes = (await db.MarketPrices.AsNoTracking()
                .Where(m => securityIds.Contains(m.SecurityId) && m.Date >= since)
                .Select(m => new { m.SecurityId, m.Date, m.Close, m.Currency })
                .ToListAsync(ct))
            .ToLookup(m => (m.SecurityId, m.Currency), m => (m.Date, m.Close));
        // Coins: the price 24 hours before the latest quote, stored with the coin's listing on each refresh.
        var references = await db.PriceListings.AsNoTracking()
            .Where(l => securityIds.Contains(l.SecurityId) && l.Reference24hPrice != null)
            .Select(l => new { l.SecurityId, l.Reference24hPrice, l.Reference24hAtUtc })
            .ToDictionaryAsync(l => l.SecurityId, ct);
        return rows.Select(r =>
        {
            var factor = factors.GetValueOrDefault(r.s.Currency);
            var account = accounts[r.p.AccountId];
            var crypto = r.s.EffectiveAssetClass == AssetClass.Crypto;
            var reference = crypto && references.TryGetValue(r.s.Id, out var l)
                ? DayChange.Reference24h(l.Reference24hPrice, l.Reference24hAtUtc, r.p.PriceAsOfUtc)
                : null;
            var previous = reference ?? DayChange.PreviousClose(closes[(r.s.Id, r.s.Currency)],
                DayChange.TradingDay(DateOnly.FromDateTime(r.p.PriceAsOfUtc.UtcDateTime), tradesEveryDay: crypto));
            return new Holding(r.p.AccountId, account.Name, r.p.Source, r.s.Id, r.s.Symbol, r.s.Isin, r.s.Name,
                r.s.Currency, r.s.EffectiveAssetClass, r.p.Quantity, r.p.AveragePrice, r.p.LastPrice,
                decimal.Round(r.p.MarketValue * factor, 2), decimal.Round(r.p.CostBasis * factor, 2),
                r.p.PriceAsOfUtc, DayChange.Amount(r.p.Quantity, r.p.LastPrice, previous, factor),
                reference is null ? DayChangeBasis.PreviousClose : DayChangeBasis.Rolling24Hours);
        }).ToList();
    }

    /// <summary>Same security at several brokers is merged by ISIN; each broker holding stays visible.</summary>
    public async Task<IReadOnlyList<PositionLine>> PositionsAsync(PortfolioScope scope, CancellationToken ct)
    {
        var holdings = await HoldingsAsync(scope, ct);
        var cash = await CashAsync(scope, ct);
        var total = holdings.Sum(h => h.MarketValueBase) + cash.Sum(c => c.Value);
        return holdings.GroupBy(h => h.SecurityId)
            .Select(g =>
            {
                var first = g.First();
                var qty = g.Sum(h => h.Quantity);
                var mv = g.Sum(h => h.MarketValueBase);
                var cost = g.Sum(h => h.CostBase);
                var day = SumKnown(g.Select(h => h.DayChangeBase));
                return new PositionLine(first.SecurityId, first.Symbol, first.Isin, first.Name, first.Currency,
                    first.AssetClass, qty, qty == 0 ? 0 : decimal.Round(g.Sum(h => h.Quantity * h.AveragePrice) / qty, 4),
                    first.LastPrice, mv, cost, mv - cost, cost == 0 ? null : decimal.Round((mv - cost) / cost, 6),
                    total == 0 ? 0 : decimal.Round(mv / total, 6),
                    g.Select(h => new PositionHolding(h.AccountId, h.AccountName, h.Broker, h.Quantity, h.AveragePrice))
                        .ToList(),
                    day, DayChange.Percent(day, mv), DayChange.Combine(g.Select(h => h.DayChangeBasis)));
            })
            .OrderByDescending(p => p.MarketValueBase)
            .ToList();
    }

    /// <summary>Sum of the known values; null when none is known (e.g. before a second day of prices).</summary>
    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(v => v is not null).ToList();
        return known.Count == 0 ? null : known.Sum();
    }

    public Task<PortfolioSummary> SummaryAsync(PortfolioScope scope, CancellationToken ct) =>
        SummaryAsync(scope, ReturnPeriod.All, ct);

    /// <summary>
    /// Totals of the scope and of each account, with the return over <paramref name="period"/> (see
    /// <see cref="PeriodReturn"/>). Coins' day change is over a rolling 24 hours when their 24-hour price is known.
    /// </summary>
    public async Task<PortfolioSummary> SummaryAsync(PortfolioScope scope, ReturnPeriod period, CancellationToken ct)
    {
        var accounts = await BrokerAccountsAsync(scope, ct);
        var ids = accounts.Keys.ToList();
        var holdings = await HoldingsAsync(scope, ct);
        var cash = await CashAsync(scope, ct);
        var flows = await db.CashMovements.AsNoTracking()
            .Where(c => ids.Contains(c.AccountId))
            .GroupBy(c => new { c.AccountId, c.Type })
            .Select(g => new { g.Key.AccountId, g.Key.Type, Base = g.Sum(c => c.BaseAmount) })
            .ToListAsync(ct);
        var realized = await db.Trades.AsNoTracking().Where(t => ids.Contains(t.AccountId))
            .SumAsync(t => t.RealizedPnlBase ?? 0, ct);
        var tradeCosts = await db.Trades.AsNoTracking().Where(t => ids.Contains(t.AccountId))
            .SumAsync(t => t.CostsBase, ct);
        var dividends = await db.Dividends.AsNoTracking().Where(d => ids.Contains(d.AccountId))
            .SumAsync(d => d.NetBaseAmount, ct);
        var lastSync = await db.Positions.AsNoTracking().Where(p => ids.Contains(p.AccountId))
            .MaxAsync(p => (DateTimeOffset?)p.SyncedAtUtc, ct);
        var since = await InceptionAsync(ids, ct);

        var marketValue = holdings.Sum(h => h.MarketValueBase);
        var cashTotal = cash.Sum(c => c.Value);
        var contributionsByAccount = flows
            .Where(f => f.Type is CashMovementType.Deposit or CashMovementType.Withdrawal)
            .GroupBy(f => f.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Base));
        var contributions = contributionsByAccount.Values.Sum();
        var fees = tradeCosts - flows.Where(f => f.Type == CashMovementType.Fee).Sum(f => f.Base);
        var totalValue = marketValue + cashTotal;
        var dayChange = SumKnown(holdings.Select(h => h.DayChangeBase));
        var totals = accounts.Values.Select(a => AccountTotalOf(a, holdings.Where(h => h.AccountId == a.Id).ToList(),
            cash.Where(c => c.AccountId == a.Id).Sum(c => c.Value), contributionsByAccount.GetValueOrDefault(a.Id),
            since.TryGetValue(a.Id, out var first) ? first : null)).ToList();
        var totalReturnPercent =
            contributions > 0 ? decimal.Round((totalValue - contributions) / contributions, 6) : (decimal?)null;
        DateOnly? firstDay = since.Count == 0 ? null : since.Values.Min();
        var periodReturn = new PeriodReturn("ALL", totalValue - contributions, totalReturnPercent, firstDay, false,
            false);

        if (period != ReturnPeriod.All)
        {
            var (valuations, deposits) = await HistoryAsync(ids, ct);
            var live = totals.Select(t => new AccountValuation(t.AccountId, Today, t.MarketValue + t.Cash)).ToList();
            valuations = WithLiveValues(valuations, live, Today);
            var baseDate = PeriodReturns.BaseDate(period, Today)!.Value;
            periodReturn = Measured(period, PeriodReturns.Compute(valuations, deposits, baseDate, Today));
            totals = totals.Select(t => t with
            {
                PeriodReturn = Measured(period, PeriodReturns.Compute(
                    valuations.Where(v => v.AccountId == t.AccountId).ToList(),
                    deposits.Where(f => f.AccountId == t.AccountId).ToList(), baseDate, Today)),
            }).ToList();
        }

        return new PortfolioSummary(
            totalValue, marketValue, cashTotal, contributions, totalValue - contributions, totalReturnPercent,
            realized, holdings.Sum(h => h.MarketValueBase - h.CostBase), dividends, fees, holdings.Count, lastSync,
            totals, dayChange, DayChange.Percent(dayChange, marketValue), firstDay, periodReturn,
            DayChange.Combine(holdings.Select(h => h.DayChangeBasis)));
    }

    private static PeriodReturn Measured(ReturnPeriod period, PeriodResult? result) =>
        new(PeriodReturns.Code(period), result?.Gain, result?.TimeWeightedReturn, result?.From,
            result?.Partial ?? false, true);

    /// <summary>The summary's figures for one account.</summary>
    private static AccountTotal AccountTotalOf(BrokerAccount account, List<Holding> holdings, decimal cash,
        decimal contributions, DateOnly? since)
    {
        var marketValue = holdings.Sum(h => h.MarketValueBase);
        var total = marketValue + cash;
        var day = SumKnown(holdings.Select(h => h.DayChangeBase));
        var percent = contributions > 0 ? decimal.Round((total - contributions) / contributions, 6) : (decimal?)null;
        return new AccountTotal(account.Id, account.Name, account.Broker, marketValue, cash, contributions,
            total - contributions, percent, day, DayChange.Percent(day, marketValue), holdings.Count, since,
            new PeriodReturn("ALL", total - contributions, percent, since, false, false),
            DayChange.Combine(holdings.Select(h => h.DayChangeBasis)));
    }

    /// <summary>First known day of each account: its first cash movement, trade or valuation.</summary>
    private async Task<Dictionary<Guid, DateOnly>> InceptionAsync(List<Guid> ids, CancellationToken ct)
    {
        var firstMovement = await db.CashMovements.AsNoTracking().Where(c => ids.Contains(c.AccountId))
            .GroupBy(c => c.AccountId).Select(g => new { g.Key, At = g.Min(c => c.OccurredAtUtc) }).ToListAsync(ct);
        var firstTrade = await db.Trades.AsNoTracking().Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => t.AccountId).Select(g => new { g.Key, At = g.Min(t => t.ExecutedAtUtc) }).ToListAsync(ct);
        var firstValuation = await db.PortfolioSnapshots.AsNoTracking().Where(s => ids.Contains(s.AccountId))
            .GroupBy(s => s.AccountId).Select(g => new { g.Key, Date = g.Min(s => s.Date) }).ToListAsync(ct);
        return firstMovement.Select(m => (m.Key, Date: DateOnly.FromDateTime(m.At.UtcDateTime)))
            .Concat(firstTrade.Select(t => (t.Key, Date: DateOnly.FromDateTime(t.At.UtcDateTime))))
            .Concat(firstValuation.Select(v => (v.Key, v.Date)))
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Date));
    }

    /// <summary>Daily account values (snapshots) and deposits/withdrawals of the accounts, up to today.</summary>
    private async Task<(List<AccountValuation> Valuations, List<AccountFlow> Flows)> HistoryAsync(List<Guid> ids,
        CancellationToken ct)
    {
        var today = Today;
        var valuations = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.AccountId) && s.Date <= today)
            .Select(s => new AccountValuation(s.AccountId, s.Date, s.MarketValueBase + s.CashBase))
            .ToListAsync(ct);
        var flows = (await db.CashMovements.AsNoTracking()
                .Where(c => ids.Contains(c.AccountId) &&
                            (c.Type == CashMovementType.Deposit || c.Type == CashMovementType.Withdrawal))
                .Select(c => new { c.AccountId, c.OccurredAtUtc, c.BaseAmount })
                .ToListAsync(ct))
            .Select(f => new AccountFlow(f.AccountId, DateOnly.FromDateTime(f.OccurredAtUtc.UtcDateTime), f.BaseAmount))
            .ToList();
        return (valuations, flows);
    }

    /// <summary>
    /// Today's value of each account is its live value (current prices), so a period's return runs up to now and
    /// matches the value shown.
    /// </summary>
    public static List<AccountValuation> WithLiveValues(IEnumerable<AccountValuation> valuations,
        IReadOnlyList<AccountValuation> live, DateOnly today) =>
        valuations.Where(v => v.Date < today || live.All(l => l.AccountId != v.AccountId))
            .Concat(live)
            .ToList();

    public async Task<IReadOnlyList<AllocationLine>> AllocationAsync(PortfolioScope scope, CancellationToken ct)
    {
        var holdings = await HoldingsAsync(scope, ct);
        var cash = (await CashAsync(scope, ct)).Sum(c => c.Value);
        var targets = await db.TargetAllocations.AsNoTracking().ToDictionaryAsync(t => t.AssetClass, t => t.Percent, ct);
        var byClass = holdings.GroupBy(h => h.AssetClass).ToDictionary(g => g.Key, g => g.Sum(h => h.MarketValueBase));
        byClass[AssetClass.Cash] = byClass.GetValueOrDefault(AssetClass.Cash) + cash;
        var total = byClass.Values.Sum();
        return Enum.GetValues<AssetClass>()
            .Where(c => byClass.GetValueOrDefault(c) != 0 || targets.ContainsKey(c))
            .Select(c =>
            {
                var value = byClass.GetValueOrDefault(c);
                var actual = total == 0 ? 0 : decimal.Round(value / total, 6);
                decimal? target = targets.TryGetValue(c, out var t) ? t : null;
                return new AllocationLine(c, value, actual, target, target is null ? null : actual - target);
            })
            .OrderByDescending(l => l.Value)
            .ToList();
    }

    public async Task<DividendSummary> DividendsAsync(PortfolioScope scope, DateOnly? from, DateOnly? to,
        CancellationToken ct)
    {
        var ids = (await BrokerAccountsAsync(scope, ct)).Keys.ToList();
        var query = from d in db.Dividends.AsNoTracking()
            join s in db.Securities.AsNoTracking() on d.SecurityId equals s.Id
            where ids.Contains(d.AccountId)
            select new { d, s };
        if (from is not null)
        {
            query = query.Where(x => x.d.PaidOn >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.d.PaidOn <= to);
        }

        var rows = await query.OrderByDescending(x => x.d.PaidOn).ToListAsync(ct);
        return new DividendSummary(
            rows.Sum(r => r.d.NetBaseAmount),
            rows.GroupBy(r => YearMonth.From(r.d.PaidOn)).OrderBy(g => g.Key)
                .Select(g => new PeriodAmount(g.Key.ToString(), g.Sum(r => r.d.NetBaseAmount))).ToList(),
            rows.GroupBy(r => r.s.Id).Select(g => new SecurityAmount(g.First().s.Symbol, g.First().s.Name,
                g.Sum(r => r.d.NetBaseAmount))).OrderByDescending(x => x.Amount).ToList(),
            rows.Take(500).Select(r => new DividendLine(r.d.PaidOn, r.s.Symbol, r.s.Name, r.d.Source, r.d.GrossAmount,
                r.d.WithholdingTax, r.d.NetAmount, r.d.Currency, r.d.NetBaseAmount, r.d.WithholdingDerived)).ToList());
    }

    public Task<PerformanceReport> PerformanceAsync(PortfolioScope scope, DateOnly? from, DateOnly? to,
        CancellationToken ct) => PerformanceAsync(scope, from, to, null, ct);

    /// <summary>
    /// The value chart and returns over <paramref name="period"/>, measured exactly as the summary's period return:
    /// from the last value on or before the period's start, up to today's live value.
    /// </summary>
    public Task<PerformanceReport> PerformanceAsync(PortfolioScope scope, ReturnPeriod period, CancellationToken ct) =>
        PerformanceAsync(scope, null, null, period, ct);

    private async Task<PerformanceReport> PerformanceAsync(PortfolioScope scope, DateOnly? from, DateOnly? to,
        ReturnPeriod? period, CancellationToken ct)
    {
        var ids = (await BrokerAccountsAsync(scope, ct)).Keys.ToList();
        var end = to ?? Today;
        var snapshots = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.AccountId) && s.Date <= end)
            .Select(s => new { s.AccountId, s.Date, Value = s.MarketValueBase + s.CashBase, s.Origin, s.EstimatedHoldings })
            .ToListAsync(ct);
        var valuations = snapshots.Select(s => new AccountValuation(s.AccountId, s.Date, s.Value)).ToList();
        if (period is { } p)
        {
            var holdings = await HoldingsAsync(scope, ct);
            var cash = await CashAsync(scope, ct);
            var live = ids.Select(id => new AccountValuation(id, end,
                holdings.Where(h => h.AccountId == id).Sum(h => h.MarketValueBase) +
                cash.Where(c => c.AccountId == id).Sum(c => c.Value))).ToList();
            valuations = WithLiveValues(valuations, live, end);
            from = PeriodReturns.BaseDate(p, end) is { } baseDate
                ? PeriodReturns.StartDate(valuations, baseDate, end)
                : null;
        }
        var flows = (await db.CashMovements.AsNoTracking()
                .Where(c => ids.Contains(c.AccountId) &&
                            (c.Type == CashMovementType.Deposit || c.Type == CashMovementType.Withdrawal))
                .Select(c => new { c.AccountId, c.OccurredAtUtc, c.BaseAmount })
                .ToListAsync(ct))
            .Select(f => new AccountFlow(f.AccountId, DateOnly.FromDateTime(f.OccurredAtUtc.UtcDateTime), f.BaseAmount))
            .ToList();

        var series = PortfolioSeries.Build(valuations, flows, from, end);
        var points = series.Points;
        if (points.Count == 0)
        {
            return new PerformanceReport(from ?? end, end, null, null, 0, 0, 0, 0, []);
        }

        var netFlows = points.Skip(1).Sum(p => p.TotalFlow);
        var reconstructed = snapshots
            .Where(s => s.Origin == SnapshotOrigins.Reconstructed && s.Date >= points[0].Date)
            .ToList();
        DateOnly? reconstructedBefore = reconstructed.Count == 0 ? null : reconstructed.Max(s => s.Date).AddDays(1);
        var estimatedDays = reconstructed.Where(s => s.EstimatedHoldings > 0).Select(s => s.Date).Distinct().Count();
        return new PerformanceReport(points[0].Date, points[^1].Date,
            Performance.TimeWeightedReturn(points.Select(p => p.ToValuation()).ToList()),
            Performance.Xirr(series.InvestorFlows), points[0].Value, points[^1].Value, netFlows,
            points[^1].Value - points[0].Value - netFlows,
            Thin(points.Select(p => new ValuePoint(p.Date, p.Value, p.CumulativeContributions)).ToList()),
            reconstructedBefore, estimatedDays);
    }

    public async Task<IReadOnlyList<(Guid AccountId, decimal Value)>> CashAsync(PortfolioScope scope,
        CancellationToken ct)
    {
        var ids = (await BrokerAccountsAsync(scope, ct)).Keys.ToList();
        var balances = await db.CashBalances.AsNoTracking().Where(c => ids.Contains(c.AccountId)).ToListAsync(ct);
        var factors = await fx.EurPerUnitAsync(balances.Select(b => b.Currency), Today, ct);
        return balances.Select(b => (b.AccountId, decimal.Round(b.Amount * factors.GetValueOrDefault(b.Currency), 2)))
            .ToList();
    }

    public sealed record BrokerAccount(Guid Id, string Name, DataSource Broker);

    public async Task<Dictionary<Guid, BrokerAccount>> BrokerAccountsAsync(PortfolioScope scope, CancellationToken ct)
    {
        var accounts = await finance.Accounts.AsNoTracking()
            .Where(a => a.Kind == AccountKind.Broker && (scope.AccountId == null || a.Id == scope.AccountId))
            .Select(a => new { a.Id, a.Name, a.Institution, a.ArchivedAtUtc })
            .ToListAsync(ct);
        var sources = await db.Positions.AsNoTracking().Select(p => new { p.AccountId, p.Source }).Distinct()
            .ToListAsync(ct);
        var cashSources = await db.CashMovements.AsNoTracking().Select(c => new { c.AccountId, c.Source }).Distinct()
            .ToListAsync(ct);
        DataSource SourceOf(Guid id, string? institution) =>
            sources.FirstOrDefault(s => s.AccountId == id)?.Source
            ?? cashSources.FirstOrDefault(s => s.AccountId == id)?.Source
            ?? (institution?.Contains("Interactive", StringComparison.OrdinalIgnoreCase) == true
                ? DataSource.InteractiveBrokers
                : institution?.Contains("Binance", StringComparison.OrdinalIgnoreCase) == true
                    ? DataSource.Binance
                    : DataSource.Trading212);

        // An archived account with nothing left in it (a purged connection, an emptied crypto location) is gone.
        return accounts
            .Where(a => a.ArchivedAtUtc == null || sources.Any(s => s.AccountId == a.Id) ||
                        cashSources.Any(s => s.AccountId == a.Id))
            .Select(a => new BrokerAccount(a.Id, a.Name, SourceOf(a.Id, a.Institution)))
            .Where(a => scope.Broker is null || a.Broker == scope.Broker)
            .ToDictionary(a => a.Id);
    }

    /// <summary>Keeps charts light: at most ~400 points, always including the last one.</summary>
    private static List<ValuePoint> Thin(List<ValuePoint> series)
    {
        if (series.Count <= 400)
        {
            return series;
        }

        var step = (int)Math.Ceiling(series.Count / 400.0);
        var thinned = series.Where((_, i) => i % step == 0).ToList();
        if (thinned[^1] != series[^1])
        {
            thinned.Add(series[^1]);
        }

        return thinned;
    }
}
