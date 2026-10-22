using System.Globalization;
using Finance.Application.Abstractions;
using Reporting.Application.Queries;
using SharedKernel;

namespace Reporting.Tests;

/// <summary>Deposits synced at a broker add to its bucket only what the ledger has not already recorded for it.</summary>
public sealed class BrokerTopUpTests
{
    [Theory]
    [InlineData("600", "0", "600")]     // only the broker knows: all of it counts
    [InlineData("600", "400", "200")]   // part recorded by hand: the rest
    [InlineData("600", "600", "0")]     // all recorded: nothing extra
    [InlineData("600", "700", "0")]     // more recorded than deposited: the ledger wins
    [InlineData("-100", "0", "0")]      // more withdrawn than deposited: never negative
    [InlineData("600", "-50", "600")]   // a net sale recorded for that broker does not inflate it
    public void Counts_the_same_money_once(string deposited, string recorded, string expected) =>
        LedgerAggregates.BrokerTopUp(D(deposited), D(recorded)).ShouldBe(D(expected));

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void Buys_count_as_invested_up_to_the_cash_transferred()
    {
        var broker = Guid.NewGuid();
        var invested = LedgerAggregates.BrokerCashInvested(
            [(new DateOnly(2026, 9, 19), 500m), (new DateOnly(2026, 9, 26), -47.73m), (new DateOnly(2026, 10, 21), 500m)],
            [
                new BrokerTradeDay(broker, new DateOnly(2026, 9, 1), 900m),    // before any transfer: its own deposit
                new BrokerTradeDay(broker, new DateOnly(2026, 9, 20), 300m),
                new BrokerTradeDay(broker, new DateOnly(2026, 9, 30), -100m),  // a sale: proceeds bought again don't count
                new BrokerTradeDay(broker, new DateOnly(2026, 10, 2), 200m),   // only 152.27 left
                new BrokerTradeDay(broker, new DateOnly(2026, 10, 22), 120m),
            ]);

        invested.ShouldBe(new Dictionary<YearMonth, decimal>
        {
            [new YearMonth(2026, 9)] = 300m,
            [new YearMonth(2026, 10)] = 272.27m,
        });
    }
}
