using System.Text.RegularExpressions;
using Investments.Domain;
using SharedKernel;

namespace Investments.Application.Prices;

/// <summary>
/// Turns what brokers say about a listing into provider-neutral hints (symbol root + MIC exchange code), so a
/// price provider can prefer the listing the user actually holds (Trading 212 "VWCEd_EQ" → VWCE on Xetra).
/// </summary>
public static partial class ListingHints
{
    public const string UnitedStates = "US";

    /// <summary>Pseudo exchange for coins entered by hand: the root is the coin id ("BTC"), quoted 24/7.</summary>
    public const string Crypto = "CRYPTO";

    // Trading 212 tickers: ROOT + lower-case exchange letter + "_EQ" (Europe), or ROOT + "_US_EQ".
    [GeneratedRegex("^(?<root>[A-Z0-9.]+?)(?<ex>[a-z])?_(?:(?<country>[A-Z]{2})_)?EQ$", RegexOptions.CultureInvariant)]
    private static partial Regex Trading212Ticker();

    private static readonly Dictionary<char, string> Trading212Exchanges = new()
    {
        ['d'] = "XETR",
        ['l'] = "XLON",
        ['a'] = "XAMS",
        ['p'] = "XPAR",
        ['m'] = "XMIL",
        ['s'] = "XSWX",
        ['e'] = "XMAD",
    };

    private static readonly Dictionary<string, string> IbkrExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IBIS"] = "XETR",
        ["IBIS2"] = "XETR",
        ["LSE"] = "XLON",
        ["LSEETF"] = "XLON",
        ["AEB"] = "XAMS",
        ["SBF"] = "XPAR",
        ["BVME"] = "XMIL",
        ["BVME.ETF"] = "XMIL",
        ["EBS"] = "XSWX",
        ["BM"] = "XMAD",
        ["NASDAQ"] = UnitedStates,
        ["NYSE"] = UnitedStates,
        ["ARCA"] = UnitedStates,
        ["AMEX"] = UnitedStates,
        ["BATS"] = UnitedStates,
    };

    public static IReadOnlyList<(string Root, string Exchange)> For(Security security,
        IEnumerable<(DataSource Source, string Symbol)> brokerSymbols)
    {
        var hints = new List<(string, string)>();
        foreach (var (source, symbol) in brokerSymbols)
        {
            if (source == DataSource.Trading212 && FromTrading212(symbol) is { } hint)
            {
                hints.Add(hint);
            }
            else if (source is DataSource.Manual or DataSource.Binance &&
                     security.EffectiveAssetClass == AssetClass.Crypto)
            {
                hints.Add((symbol.ToUpperInvariant(), Crypto));
            }
        }

        if (security.Exchange is { } exchange && IbkrExchanges.TryGetValue(exchange, out var mic))
        {
            hints.Add((security.Symbol.ToUpperInvariant(), mic));
        }

        return hints.Distinct().ToList();
    }

    public static (string Root, string Exchange)? FromTrading212(string ticker)
    {
        var match = Trading212Ticker().Match(ticker);
        if (!match.Success)
        {
            return null;
        }

        var root = match.Groups["root"].Value;
        if (match.Groups["country"].Success)
        {
            return match.Groups["country"].Value == UnitedStates ? (root, UnitedStates) : null;
        }

        return match.Groups["ex"].Success && Trading212Exchanges.TryGetValue(match.Groups["ex"].Value[0], out var mic)
            ? (root, mic)
            : null;
    }
}
