using Finance.Domain.Transactions;
using SharedKernel;

namespace Finance.Tests;

public sealed class InvestmentEntryTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Bucket = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 26);

    private static Result<Transaction> Create(TransactionType type, InvestmentAsset? asset, Guid? bucket = null) =>
        Transaction.Create(new TransactionDraft(type, Day, 500m, "EUR", Account, BucketId: bucket ?? Bucket,
            Asset: asset), DataSource.Manual);

    [Theory]
    [InlineData(TransactionType.Expense, TransactionFlow.Everyday)]
    [InlineData(TransactionType.Income, TransactionFlow.Everyday)]
    [InlineData(TransactionType.InvestmentContribution, TransactionFlow.Investment)]
    [InlineData(TransactionType.InvestmentSale, TransactionFlow.Investment)]
    [InlineData(TransactionType.Transfer, TransactionFlow.Movement)]
    [InlineData(TransactionType.Savings, TransactionFlow.Movement)]
    public void Every_type_belongs_to_exactly_one_flow(TransactionType type, TransactionFlow flow)
    {
        TransactionTypes.FlowOf(type).ShouldBe(flow);
        TransactionTypes.Of(flow).ShouldContain(type);
    }

    [Fact]
    public void Purchase_keeps_the_instrument_details()
    {
        var t = Create(TransactionType.InvestmentContribution,
            new InvestmentAsset(InvestmentAssetKind.Etf, " vwce ", "Vanguard FTSE All-World", "ie00bk5bqt80", 4m,
                125m, AssetPriceSources.Trading212)).Value;

        t.AssetKind.ShouldBe(InvestmentAssetKind.Etf);
        t.AssetSymbol.ShouldBe("VWCE");
        t.AssetIsin.ShouldBe("IE00BK5BQT80");
        t.AssetQuantity.ShouldBe(4m);
        t.AssetUnitPrice.ShouldBe(125m);
        t.AssetPriceSource.ShouldBe(AssetPriceSources.Trading212);
    }

    [Fact]
    public void Crypto_is_always_recorded_as_manually_entered()
    {
        var t = Create(TransactionType.InvestmentContribution,
            new InvestmentAsset(InvestmentAssetKind.Crypto, "BTC", "Bitcoin", Quantity: 0.01m, UnitPrice: 50_000m,
                PriceSource: AssetPriceSources.InteractiveBrokers)).Value;

        t.AssetPriceSource.ShouldBe(AssetPriceSources.Manual);
    }

    [Fact]
    public void Sale_requires_an_investment_bucket_like_a_purchase()
    {
        var t = Transaction.Create(new TransactionDraft(TransactionType.InvestmentSale, Day, 100m, "EUR", Account),
            DataSource.Manual);

        t.Error.Code.ShouldBe("Transaction.Bucket");
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("AAPL", 0, 1)]
    [InlineData("AAPL", 1, -1)]
    public void Invalid_asset_details_are_rejected(string symbol, decimal quantity, decimal price) =>
        Create(TransactionType.InvestmentSale, new InvestmentAsset(InvestmentAssetKind.Stock, symbol,
            Quantity: quantity, UnitPrice: price)).Error.Code.ShouldBe("Transaction.Asset");

    [Fact]
    public void Everyday_entries_never_carry_asset_details()
    {
        var t = Transaction.Create(new TransactionDraft(TransactionType.Expense, Day, 20m, "EUR", Account,
            CategoryId: Guid.NewGuid(), Asset: new InvestmentAsset(InvestmentAssetKind.Stock, "AAPL")),
            DataSource.Manual).Value;

        t.AssetSymbol.ShouldBeNull();
        t.AssetKind.ShouldBeNull();
    }
}
