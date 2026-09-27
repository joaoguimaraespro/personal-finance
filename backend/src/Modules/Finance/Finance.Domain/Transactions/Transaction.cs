using Finance.Domain.Categories;
using SharedKernel;

namespace Finance.Domain.Transactions;

public enum TransactionType
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
    Savings = 3,
    InvestmentContribution = 4,

    /// <summary>Money coming back from an investment (asset sold). Counts as negative investment, never as income.</summary>
    InvestmentSale = 5,
}

/// <summary>
/// The two worlds the ledger keeps apart: day-to-day money (income and expenses) and investing (buying and selling
/// assets). Transfers and savings only move money between the owner's own accounts and buckets.
/// </summary>
public enum TransactionFlow
{
    Everyday = 0,
    Investment = 1,
    Movement = 2,
}

public static class TransactionTypes
{
    public static readonly IReadOnlyList<TransactionType> Everyday = [TransactionType.Expense, TransactionType.Income];

    public static readonly IReadOnlyList<TransactionType> Investment =
        [TransactionType.InvestmentContribution, TransactionType.InvestmentSale];

    public static readonly IReadOnlyList<TransactionType> Movement = [TransactionType.Transfer, TransactionType.Savings];

    public static TransactionFlow FlowOf(TransactionType type) => type switch
    {
        TransactionType.Expense or TransactionType.Income => TransactionFlow.Everyday,
        TransactionType.InvestmentContribution or TransactionType.InvestmentSale => TransactionFlow.Investment,
        _ => TransactionFlow.Movement,
    };

    public static IReadOnlyList<TransactionType> Of(TransactionFlow flow) => flow switch
    {
        TransactionFlow.Everyday => Everyday,
        TransactionFlow.Investment => Investment,
        _ => Movement,
    };

    public static bool IsInvestment(TransactionType type) => FlowOf(type) == TransactionFlow.Investment;
}

public enum InvestmentAssetKind
{
    Stock = 0,
    Etf = 1,
    Crypto = 2,
    Fund = 3,
    Bond = 4,
    Other = 5,
}

/// <summary>Where the instrument details of an investment entry came from. Crypto is always entered by hand.</summary>
public static class AssetPriceSources
{
    public const string Manual = "Manual";
    public const string Trading212 = "Trading212";
    public const string InteractiveBrokers = "InteractiveBrokers";
    public const string Portfolio = "Portfolio";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Manual, Trading212, InteractiveBrokers, Portfolio };
}

/// <summary>What was bought or sold. Quantity and unit price are in the transaction's original currency.</summary>
public sealed record InvestmentAsset(
    InvestmentAssetKind Kind,
    string Symbol,
    string? Name = null,
    string? Isin = null,
    decimal? Quantity = null,
    decimal? UnitPrice = null,
    string? PriceSource = null);

/// <summary>
/// The single ledger that replaces the spreadsheet's twelve month sheets. Amounts are always positive;
/// direction comes from <see cref="Type"/>. Monthly and annual figures are computed from these rows.
/// </summary>
public sealed class Transaction : Entity, IAuditable, ISoftDeletable
{
    public const string DefaultTimeZone = "Europe/Lisbon";

    private Transaction() { }

    public TransactionType Type { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public DateTimeOffset? OccurredAtUtc { get; private set; }
    public string TimeZone { get; private set; } = DefaultTimeZone;

    public Guid AccountId { get; private set; }

    /// <summary>Destination for transfers, savings and investment contributions (optional for the latter two).</summary>
    public Guid? CounterAccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public ExpenseNature? Nature { get; private set; }
    public Guid? BucketId { get; private set; }
    public Guid? GoalId { get; private set; }

    /// <summary>Instrument details, only on investment entries and optional (older entries have none).</summary>
    public InvestmentAssetKind? AssetKind { get; private set; }
    public string? AssetSymbol { get; private set; }
    public string? AssetName { get; private set; }
    public string? AssetIsin { get; private set; }
    public decimal? AssetQuantity { get; private set; }
    public decimal? AssetUnitPrice { get; private set; }
    public string? AssetPriceSource { get; private set; }

    public decimal OriginalAmount { get; private set; }
    public string OriginalCurrency { get; private set; } = Currency.Base;
    public decimal FxRate { get; private set; } = 1m;
    public decimal BaseAmount { get; private set; }
    public string BaseCurrency { get; private set; } = Currency.Base;

    public string? Description { get; private set; }
    public string? Notes { get; private set; }

    public DataSource Source { get; private set; }
    public Guid? ImportId { get; private set; }
    public string? ExternalId { get; private set; }
    public Guid? ExpectedTransactionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public static Result<Transaction> Create(TransactionDraft draft, DataSource source, Guid? importId = null,
        string? externalId = null, Guid? expectedTransactionId = null)
    {
        var transaction = new Transaction
        {
            Source = source,
            ImportId = importId,
            ExternalId = externalId,
            ExpectedTransactionId = expectedTransactionId,
        };
        var applied = transaction.Apply(draft);
        return applied.IsSuccess ? transaction : Result.Failure<Transaction>(applied.Error);
    }

    public Result Update(TransactionDraft draft) => Apply(draft);

    public void SoftDelete(DateTimeOffset now) => DeletedAtUtc = now;

    public void Restore() => DeletedAtUtc = null;

    private Result Apply(TransactionDraft d)
    {
        var error = TransactionRules.Validate(d);
        if (error is not null)
        {
            return error;
        }

        Type = d.Type;
        OccurredOn = d.OccurredOn;
        OccurredAtUtc = d.OccurredAtUtc;
        TimeZone = string.IsNullOrWhiteSpace(d.TimeZone) ? DefaultTimeZone : d.TimeZone;
        AccountId = d.AccountId;
        CounterAccountId = d.CounterAccountId;
        CategoryId = d.Type is TransactionType.Expense or TransactionType.Income ? d.CategoryId : null;
        Nature = d.Type == TransactionType.Expense ? d.Nature ?? ExpenseNature.Variable : null;
        BucketId = d.Type == TransactionType.Savings || TransactionTypes.IsInvestment(d.Type) ? d.BucketId : null;
        GoalId = d.Type == TransactionType.Savings ? d.GoalId : null;
        ApplyAsset(TransactionTypes.IsInvestment(d.Type) ? d.Asset : null);
        OriginalAmount = d.Amount;
        OriginalCurrency = d.Currency;
        FxRate = d.Currency == Currency.Base ? 1m : d.FxRate!.Value;
        BaseAmount = d.Currency == Currency.Base ? d.Amount : decimal.Round(d.Amount * FxRate, 4);
        BaseCurrency = Currency.Base;
        Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
        Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();
        return Result.Success();
    }

    private void ApplyAsset(InvestmentAsset? a)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        AssetKind = a?.Kind;
        AssetSymbol = Clean(a?.Symbol)?.ToUpperInvariant();
        AssetName = Clean(a?.Name);
        AssetIsin = Clean(a?.Isin)?.ToUpperInvariant();
        AssetQuantity = a?.Quantity;
        AssetUnitPrice = a?.UnitPrice;
        // Crypto has no automatic price provider: whatever the client claims, it was typed in by hand.
        AssetPriceSource = a switch
        {
            null => null,
            { Kind: InvestmentAssetKind.Crypto } or { PriceSource: null } => AssetPriceSources.Manual,
            _ => a.PriceSource,
        };
    }
}

/// <summary>User-editable fields of a transaction. <see cref="FxRate"/> converts 1 unit of <see cref="Currency"/> to EUR.</summary>
public sealed record TransactionDraft(
    TransactionType Type,
    DateOnly OccurredOn,
    decimal Amount,
    string Currency,
    Guid AccountId,
    Guid? CategoryId = null,
    ExpenseNature? Nature = null,
    Guid? CounterAccountId = null,
    Guid? BucketId = null,
    Guid? GoalId = null,
    decimal? FxRate = null,
    string? Description = null,
    string? Notes = null,
    DateTimeOffset? OccurredAtUtc = null,
    string? TimeZone = null,
    InvestmentAsset? Asset = null);

public static class TransactionErrors
{
    public static readonly Error NotFound = Error.NotFound("Transaction.NotFound", "Transaction not found.");

    public static readonly Error AmountMustBePositive =
        Error.Validation("Transaction.Amount", "Amount must be greater than zero.");

    public static readonly Error CategoryRequired =
        Error.Validation("Transaction.Category", "Expenses and income require a category.");

    public static readonly Error CounterAccountRequired =
        Error.Validation("Transaction.CounterAccount", "Transfers require a destination account.");

    public static readonly Error SameAccount =
        Error.Validation("Transaction.CounterAccount", "Source and destination accounts must differ.");

    public static readonly Error BucketRequired =
        Error.Validation("Transaction.Bucket", "Savings and investment entries require an allocation bucket.");

    public static readonly Error InvalidAsset = Error.Validation("Transaction.Asset",
        "An investment asset needs a symbol; quantity must be positive and the unit price cannot be negative.");

    public static readonly Error FxRateRequired =
        Error.Validation("Transaction.FxRate", "An exchange rate to EUR is required for non-EUR amounts.");

    public static readonly Error InvalidCurrency =
        Error.Validation("Transaction.Currency", "Currency must be an ISO 4217 code.");

    public static readonly Error ReadOnlySource = Error.Forbidden("Transaction.ReadOnly",
        "Records imported from a broker are read-only.");
}

internal static class TransactionRules
{
    public static Error? Validate(TransactionDraft d)
    {
        if (d.Amount <= 0)
        {
            return TransactionErrors.AmountMustBePositive;
        }

        if (!Currency.IsValid(d.Currency))
        {
            return TransactionErrors.InvalidCurrency;
        }

        if (d.Currency != Currency.Base && d.FxRate is not > 0)
        {
            return TransactionErrors.FxRateRequired;
        }

        return d.Type switch
        {
            TransactionType.Expense or TransactionType.Income when d.CategoryId is null =>
                TransactionErrors.CategoryRequired,
            TransactionType.Transfer when d.CounterAccountId is null => TransactionErrors.CounterAccountRequired,
            TransactionType.Savings or TransactionType.InvestmentContribution or TransactionType.InvestmentSale
                when d.BucketId is null => TransactionErrors.BucketRequired,
            _ when d.CounterAccountId == d.AccountId => TransactionErrors.SameAccount,
            _ when TransactionTypes.IsInvestment(d.Type) && d.Asset is { } a && !IsValid(a) =>
                TransactionErrors.InvalidAsset,
            _ => null,
        };
    }

    private static bool IsValid(InvestmentAsset a) =>
        Enum.IsDefined(a.Kind) &&
        !string.IsNullOrWhiteSpace(a.Symbol) &&
        a.Quantity is null or > 0 &&
        a.UnitPrice is null or >= 0 &&
        (a.PriceSource is null || AssetPriceSources.All.Contains(a.PriceSource));
}
