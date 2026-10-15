using Investments.Application.Logos;
using Investments.Domain;

namespace Investments.Tests;

public sealed class SecurityLogoTests
{
    [Theory]
    [InlineData("AAPL", AssetClass.Stock, "AAPL")]
    [InlineData("brk.b", AssetClass.Stock, "BRK.B")]
    [InlineData("SPY", AssetClass.Etf, "SPY")]
    [InlineData("VWCE.DE", AssetClass.Etf, null)]  // exchange-suffixed listings have no logo at the source
    [InlineData("BTC", AssetClass.Crypto, null)]   // coins use the bundled coin icons
    [InlineData("IE00BK5BQT80", AssetClass.Etf, null)]
    public void Only_plain_tickers_of_shares_and_etfs_are_looked_up(string symbol, AssetClass cls, string? expected) =>
        SecurityLogoService.TickerOf(Security.Create(null, symbol, null, symbol, "USD", cls)).ShouldBe(expected);

    [Fact]
    public void A_missing_logo_is_asked_again_sooner_than_a_found_one_is_refreshed()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var missing = SecurityLogo.Create(Guid.NewGuid());
        missing.Store(null, null, t0);
        var found = SecurityLogo.Create(Guid.NewGuid());
        found.Store([1, 2, 3], "image/png", t0);

        missing.IsStale(t0.AddDays(31)).ShouldBeTrue();
        found.IsStale(t0.AddDays(31)).ShouldBeFalse();
        found.IsStale(t0.AddDays(91)).ShouldBeTrue();
    }
}
