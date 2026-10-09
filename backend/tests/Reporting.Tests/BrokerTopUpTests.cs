using System.Globalization;
using Reporting.Application.Queries;

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
}
