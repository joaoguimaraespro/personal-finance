using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Fx;
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
    DateTimeOffset PriceAsOfUtc);

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
    IReadOnlyList<PositionHolding> Holdings);

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
    IReadOnlyList<AccountTotal> Accounts);

public sealed record AccountTotal(Guid AccountId, string Name, DataSource Broker, decimal MarketValue, decimal Cash);

public sealed record AllocationLine(AssetClass AssetClass, decimal Value, decimal Actual, decimal? Target,
    decimal? Difference);

public sealed record DividendLine(DateOnly PaidOn, string Symbol, string Name, DataSource Broker, decimal Gross,
    decimal WithholdingTax, decimal Net, string Currency, decimal NetBase, bool WithholdingDerived);

public sealed record DividendSummary(decimal TotalNetBase, IReadOnlyList<PeriodAmount> ByMonth,
    IReadOnlyList<SecurityAmount> BySecurity, IReadOnlyList<DividendLine> Items);

public sealed record PeriodAmount(string Period, decimal Amount);

public sealed record SecurityAmount(string Symbol, string Name, decimal Amount);

public sealed record PerformanceReport(
    DateOnly From,
    DateOnly To,
    decimal? TimeWeightedReturn,
    decimal? MoneyWeightedReturn,
    decimal StartValue,
    decimal EndValue,
    decimal NetFlows,
    decimal Gain,
    IReadOnlyList<ValuePoint> Series);

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
        return rows.Select(r =>
        {
            var factor = factors.GetValueOrDefault(r.s.Currency);
            var account = accounts[r.p.AccountId];
            return new Holding(r.p.AccountId, account.Name, r.p.Source, r.s.Id, r.s.Symbol, r.s.Isin, r.s.Name,
                r.s.Currency, r.s.EffectiveAssetClass, r.p.Quantity, r.p.AveragePrice, r.p.LastPrice,
                decimal.Round(r.p.MarketValue * factor, 2), decimal.Round(r.p.CostBasis * factor, 2),
                r.p.PriceAsOfUtc);
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
                return new PositionLine(first.SecurityId, first.Symbol, first.Isin, first.Name, first.Currency,
                    first.AssetClass, qty, qty == 0 ? 0 : decimal.Round(g.Sum(h => h.Quantity * h.AveragePrice) / qty, 4),
                    first.LastPrice, mv, cost, mv - cost, cost == 0 ? null : decimal.Round((mv - cost) / cost, 6),
                    total == 0 ? 0 : decimal.Round(mv / total, 6),
                    g.Select(h => new PositionHolding(h.AccountId, h.AccountName, h.Broker, h.Quantity, h.AveragePrice))
                        .ToList());
            })
            .OrderByDescending(p => p.MarketValueBase)
            .ToList();
    }

    public async Task<PortfolioSummary> SummaryAsync(PortfolioScope scope, CancellationToken ct)
    {
        var accounts = await BrokerAccountsAsync(scope, ct);
        var ids = accounts.Keys.ToList();
        var holdings = await HoldingsAsync(scope, ct);
        var cash = await CashAsync(scope, ct);
        var flows = await db.CashMovements.AsNoTracking()
            .Where(c => ids.Contains(c.AccountId))
            .GroupBy(c => c.Type)
            .Select(g => new { Type = g.Key, Base = g.Sum(c => c.BaseAmount) })
            .ToListAsync(ct);
        var realized = await db.Trades.AsNoTracking().Where(t => ids.Contains(t.AccountId))
            .SumAsync(t => t.RealizedPnlBase ?? 0, ct);
        var tradeCosts = await db.Trades.AsNoTracking().Where(t => ids.Contains(t.AccountId))
            .SumAsync(t => t.CostsBase, ct);
        var dividends = await db.Dividends.AsNoTracking().Where(d => ids.Contains(d.AccountId))
            .SumAsync(d => d.NetBaseAmount, ct);
        var lastSync = await db.Positions.AsNoTracking().Where(p => ids.Contains(p.AccountId))
            .MaxAsync(p => (DateTimeOffset?)p.SyncedAtUtc, ct);

        var marketValue = holdings.Sum(h => h.MarketValueBase);
        var cashTotal = cash.Sum(c => c.Value);
        var contributions = flows.Where(f => f.Type is CashMovementType.Deposit or CashMovementType.Withdrawal)
            .Sum(f => f.Base);
        var fees = tradeCosts - flows.Where(f => f.Type == CashMovementType.Fee).Sum(f => f.Base);
        var totalValue = marketValue + cashTotal;

        return new PortfolioSummary(
            totalValue, marketValue, cashTotal, contributions, totalValue - contributions,
            contributions > 0 ? decimal.Round((totalValue - contributions) / contributions, 6) : null,
            realized, holdings.Sum(h => h.MarketValueBase - h.CostBase), dividends, fees, holdings.Count, lastSync,
            accounts.Values.Select(a => new AccountTotal(a.Id, a.Name, a.Broker,
                holdings.Where(h => h.AccountId == a.Id).Sum(h => h.MarketValueBase),
                cash.Where(c => c.AccountId == a.Id).Sum(c => c.Value))).ToList());
    }

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

    public async Task<PerformanceReport> PerformanceAsync(PortfolioScope scope, DateOnly? from, DateOnly? to,
        CancellationToken ct)
    {
        var ids = (await BrokerAccountsAsync(scope, ct)).Keys.ToList();
        var end = to ?? Today;
        var valuations = await db.PortfolioSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.AccountId) && s.Date <= end)
            .Select(s => new AccountValuation(s.AccountId, s.Date, s.MarketValueBase + s.CashBase))
            .ToListAsync(ct);
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
        return new PerformanceReport(points[0].Date, points[^1].Date,
            Performance.TimeWeightedReturn(points.Select(p => p.ToValuation()).ToList()),
            Performance.Xirr(series.InvestorFlows), points[0].Value, points[^1].Value, netFlows,
            points[^1].Value - points[0].Value - netFlows,
            Thin(points.Select(p => new ValuePoint(p.Date, p.Value, p.CumulativeContributions)).ToList()));
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
            .Select(a => new { a.Id, a.Name, a.Institution })
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
                : DataSource.Trading212);

        return accounts
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
