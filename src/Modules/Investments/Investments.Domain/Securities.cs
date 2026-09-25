using SharedKernel;

namespace Investments.Domain;

public enum AssetClass
{
    Stock = 0,
    Etf = 1,
    Bond = 2,
    Fund = 3,
    Crypto = 4,
    Cash = 5,
    Other = 6,
}

/// <summary>
/// A financial instrument, identified primarily by ISIN so the same ETF held at two brokers is one security
/// in the consolidated portfolio.
/// </summary>
public sealed class Security : Entity
{
    private Security() { }

    public string? Isin { get; private set; }
    public string Symbol { get; private set; } = null!;
    public string? Exchange { get; private set; }
    public string Name { get; private set; } = null!;
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public AssetClass AssetClass { get; private set; }

    /// <summary>Set by the user when broker metadata classifies an instrument wrongly (e.g. an ETF as a stock).</summary>
    public AssetClass? AssetClassOverride { get; private set; }

    public AssetClass EffectiveAssetClass => AssetClassOverride ?? AssetClass;

    public static Security Create(string? isin, string symbol, string? exchange, string name, string currency,
        AssetClass assetClass) => new()
    {
        Isin = string.IsNullOrWhiteSpace(isin) ? null : isin.Trim().ToUpperInvariant(),
        Symbol = symbol.Trim(),
        Exchange = exchange,
        Name = name.Trim(),
        Currency = currency,
        AssetClass = assetClass,
    };

    /// <summary>Broker metadata may improve over time (names, exchange); identity (ISIN) never changes.</summary>
    public void Refresh(string name, string? exchange, AssetClass assetClass)
    {
        Name = name.Trim();
        Exchange = exchange ?? Exchange;
        AssetClass = assetClass;
    }

    public void OverrideAssetClass(AssetClass? assetClass) => AssetClassOverride = assetClass;
}

/// <summary>How each broker names a security (e.g. Trading 212 "VWCEd_EQ", IBKR conid "12345").</summary>
public sealed class BrokerSymbol
{
    private BrokerSymbol() { }

    public DataSource Source { get; private set; }
    public string Symbol { get; private set; } = null!;
    public Guid SecurityId { get; private set; }

    public static BrokerSymbol Create(DataSource source, string symbol, Guid securityId) =>
        new() { Source = source, Symbol = symbol, SecurityId = securityId };
}
