using Integrations.Application.Contracts;
using Investments.Application.Sync;
using Investments.Domain;

namespace Integrations.Infrastructure.Demo;

/// <summary>
/// Fictitious, deterministic portfolio for demos, screenshots and end-to-end tests. Profile "a" and "b" both hold
/// the same world ETF so the consolidated view can be demonstrated. Only registered when explicitly enabled.
/// </summary>
internal sealed class DemoProvider(string profile, TimeProvider clock) : IInvestmentProvider
{
    private sealed record Instrument(string Ticker, string Isin, string Name, string Currency, AssetClass Class,
        decimal StartPrice, decimal Drift, decimal? QuarterlyDividendPerShare);

    private static readonly Instrument[] ProfileA =
    [
        new("VWCE", "IE00BK5BQT80", "Vanguard FTSE All-World UCITS ETF (Acc)", "EUR", AssetClass.Etf, 98m, 0.0035m, null),
        new("AGGH", "IE00BDBRDM35", "iShares Core Global Aggregate Bond UCITS ETF", "EUR", AssetClass.Bond, 4.9m, 0.0004m, null),
        new("AAPL", "US0378331005", "Apple Inc.", "USD", AssetClass.Stock, 170m, 0.004m, 0.25m),
    ];

    private static readonly Instrument[] ProfileB =
    [
        new("VWCE", "IE00BK5BQT80", "Vanguard FTSE All-World UCITS ETF (Acc)", "EUR", AssetClass.Etf, 98m, 0.0035m, null),
        new("MSFT", "US5949181045", "Microsoft Corporation", "USD", AssetClass.Stock, 330m, 0.0045m, 0.83m),
    ];

    private const decimal UsdPerEur = 1.10m;
    private const int Months = 24;

    public BrokerKind Kind => BrokerKind.Demo;

    private Instrument[] Instruments => profile == "b" ? ProfileB : ProfileA;
    private decimal MonthlyDeposit => profile == "b" ? 400m : 600m;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    private DateOnly Start => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-Months);

    private static decimal Price(Instrument i, int dayIndex) =>
        decimal.Round(i.StartPrice * (1 + i.Drift * dayIndex / 21m) * (1 + 0.04m * (decimal)Math.Sin(dayIndex / 17.0 + i.StartPrice.GetHashCode() % 7)), 4);

    private static decimal ToEur(Instrument i, decimal amount) => i.Currency == "USD" ? amount / UsdPerEur : amount;

    /// <summary>Each month: deposit, then invest it across the instruments in fixed proportions.</summary>
    private (List<TradeReport> Trades, List<CashMovementReport> Cash, Dictionary<string, decimal> Quantities, decimal CashBalance)
        Simulate(DateOnly until)
    {
        var trades = new List<TradeReport>();
        var cash = new List<CashMovementReport>();
        var quantities = Instruments.ToDictionary(i => i.Isin, _ => 0m);
        var cashBalance = 0m;
        for (var month = 0; month < Months; month++)
        {
            var day = Start.AddMonths(month).AddDays(2);
            if (day > until)
            {
                break;
            }

            var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            cash.Add(new CashMovementReport($"demo:{profile}:dep:{day:yyyyMMdd}", CashMovementType.Deposit,
                MonthlyDeposit, "EUR", at, "Monthly deposit"));
            cashBalance += MonthlyDeposit;
            var budget = MonthlyDeposit * 0.95m;
            foreach (var i in Instruments)
            {
                var share = budget / Instruments.Length;
                var price = Price(i, (day.DayNumber - Start.DayNumber));
                var qty = decimal.Round(share / ToEur(i, price), 4);
                quantities[i.Isin] += qty;
                var costEur = decimal.Round(ToEur(i, qty * price), 2) + 0.5m;
                cashBalance -= costEur;
                trades.Add(new TradeReport($"demo:{profile}:buy:{i.Ticker}:{day:yyyyMMdd}",
                    Security(i), TradeSide.Buy, qty, price, i.Currency, 0.5m, 0m, "EUR", -costEur, "EUR", null,
                    at.AddMinutes(5)));
            }
        }

        return (trades, cash, quantities, decimal.Round(cashBalance, 2));
    }

    public Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct)
    {
        var history = new List<SnapshotReport>();
        for (var d = Start.AddDays(3); d <= Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var sim = Simulate(d);
            var value = Instruments.Sum(i => ToEur(i, sim.Quantities[i.Isin] * Price(i, d.DayNumber - Start.DayNumber)));
            var flow = sim.Cash.Where(c => DateOnly.FromDateTime(c.OccurredAt.UtcDateTime) == d).Sum(c => c.Amount);
            history.Add(new SnapshotReport(d, decimal.Round(value + sim.CashBalance, 2), sim.CashBalance, "EUR", flow));
        }

        var now = Simulate(Today);
        return Task.FromResult(new AccountSnapshot("EUR", now.CashBalance, history.LastOrDefault()?.TotalValue ?? 0, history));
    }

    public Task<IReadOnlyList<PositionReport>> GetPositionsAsync(CancellationToken ct)
    {
        var sim = Simulate(Today);
        var dayIndex = Today.DayNumber - Start.DayNumber;
        IReadOnlyList<PositionReport> positions = Instruments.Select(i =>
        {
            var buys = sim.Trades.Where(t => t.Security.Isin == i.Isin).ToList();
            var qty = buys.Sum(t => t.Quantity);
            var avg = qty == 0 ? 0 : decimal.Round(buys.Sum(t => t.Quantity * t.Price) / qty, 4);
            return new PositionReport(Security(i), qty, avg, Price(i, dayIndex), clock.GetUtcNow());
        }).ToList();
        return Task.FromResult(positions);
    }

    public Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var sim = Simulate(Today);
        IReadOnlyList<InvestmentTransaction> all = sim.Trades.Select(InvestmentTransaction.Of)
            .Concat(sim.Cash.Select(InvestmentTransaction.Of)).ToList();
        return Task.FromResult(all);
    }

    public Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var dividends = new List<DividendReport>();
        foreach (var i in Instruments.Where(i => i.QuarterlyDividendPerShare is not null))
        {
            for (var q = Start.AddMonths(2); q <= Today; q = q.AddMonths(3))
            {
                var qty = Simulate(q).Quantities[i.Isin];
                var gross = decimal.Round(qty * i.QuarterlyDividendPerShare!.Value, 2);
                if (gross <= 0)
                {
                    continue;
                }

                var wht = decimal.Round(gross * 0.15m, 2);
                dividends.Add(new DividendReport($"demo:{profile}:div:{i.Ticker}:{q:yyyyMMdd}", Security(i), q.AddDays(14),
                    gross, wht, gross - wht, i.Currency, null, null));
            }
        }

        return Task.FromResult<IReadOnlyList<DividendReport>>(dividends);
    }

    private static SecurityReport Security(Instrument i) =>
        new($"DEMO_{i.Ticker}", i.Isin, i.Ticker, null, i.Name, i.Currency, i.Class);
}
