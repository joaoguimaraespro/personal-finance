using Integrations.Application.Instruments;
using Investments.Domain;

namespace Integrations.Tests;

public sealed class InstrumentRankingTests
{
    private static InstrumentMatch M(string symbol, string name, AssetClass assetClass = AssetClass.Stock,
        string? isin = null) =>
        new(InstrumentProvider.Trading212, symbol + "_US_EQ", symbol, name, isin, "USD", assetClass, null);

    private static readonly InstrumentMatch[] Catalog =
    [
        M("AAPL", "Apple"),
        M("AAPB", "GraniteShares 2x Long AAPL"),
        M("APLE", "Apple Hospitality REIT"),
        M("VWCE", "Vanguard FTSE All-World", AssetClass.Etf, "IE00BK5BQT80"),
        M("BTCE", "Bitcoin ETC", AssetClass.Crypto),
    ];

    [Fact]
    public void Exact_symbol_comes_first_then_prefixes_then_name_matches() =>
        InstrumentRanking.Rank(Catalog, "aapl", 10).Select(m => m.Symbol).ShouldBe(["AAPL", "AAPB"]);

    [Fact]
    public void Name_prefix_matches() =>
        InstrumentRanking.Rank(Catalog, "apple", 10).Select(m => m.Symbol).ShouldBe(["AAPL", "APLE"]);

    [Fact]
    public void Isin_finds_the_instrument() =>
        InstrumentRanking.Rank(Catalog, "IE00BK5BQT80", 10).Single().Symbol.ShouldBe("VWCE");

    [Fact]
    public void Crypto_is_never_suggested() =>
        InstrumentRanking.Rank(Catalog, "bitcoin", 10).ShouldBeEmpty();

    [Fact]
    public void Empty_query_returns_nothing() =>
        InstrumentRanking.Rank(Catalog, "  ", 10).ShouldBeEmpty();
}
