using SharedKernel;

namespace Investments.Domain;

public enum ManualAssetKind
{
    RealEstate = 0,
    Vehicle = 1,
    Crypto = 2,
    Pension = 3,
    Other = 4,
    Loan = 10,
    Mortgage = 11,
    OtherDebt = 12,
}

/// <summary>Things outside bank and broker accounts: a flat, a car, a pension, a mortgage. Valued by hand.</summary>
public sealed class ManualAsset : Entity, IAuditable
{
    private readonly List<AssetValuation> _valuations = [];

    private ManualAsset() { }

    public string Name { get; private set; } = null!;
    public ManualAssetKind Kind { get; private set; }
    public string Currency { get; private set; } = SharedKernel.Currency.Base;
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public IReadOnlyList<AssetValuation> Valuations => _valuations;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public bool IsLiability => Kind >= ManualAssetKind.Loan;

    public static ManualAsset Create(string name, ManualAssetKind kind, string currency) =>
        new() { Name = name.Trim(), Kind = kind, Currency = currency };

    public void Rename(string name) => Name = name.Trim();

    public void Archive(DateTimeOffset now) => ArchivedAtUtc = now;

    /// <summary>Values are stored positive; liabilities are subtracted by the net-worth calculation.</summary>
    public void Value(DateOnly on, decimal value)
    {
        var existing = _valuations.FirstOrDefault(v => v.Date == on);
        if (existing is not null)
        {
            _valuations.Remove(existing);
        }

        _valuations.Add(new AssetValuation(Id, on, Math.Abs(value)));
    }

    public decimal? ValueOn(DateOnly date) =>
        _valuations.Where(v => v.Date <= date).OrderByDescending(v => v.Date).FirstOrDefault()?.Value;
}

public sealed class AssetValuation
{
    private AssetValuation() { }

    internal AssetValuation(Guid assetId, DateOnly date, decimal value)
    {
        AssetId = assetId;
        Date = date;
        Value = value;
    }

    public Guid AssetId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Value { get; private set; }
}

public sealed class NetWorthSnapshot
{
    private NetWorthSnapshot() { }

    public DateOnly Date { get; private set; }
    public decimal AssetsBase { get; private set; }
    public decimal LiabilitiesBase { get; private set; }

    /// <summary>JSON breakdown: cash, investments, manual assets, liabilities by kind.</summary>
    public string Breakdown { get; private set; } = "{}";

    public decimal NetWorthBase => AssetsBase - LiabilitiesBase;

    public static NetWorthSnapshot Create(DateOnly date, decimal assets, decimal liabilities, string breakdown) =>
        new() { Date = date, AssetsBase = assets, LiabilitiesBase = liabilities, Breakdown = breakdown };

    public void Update(decimal assets, decimal liabilities, string breakdown)
    {
        AssetsBase = assets;
        LiabilitiesBase = liabilities;
        Breakdown = breakdown;
    }
}
