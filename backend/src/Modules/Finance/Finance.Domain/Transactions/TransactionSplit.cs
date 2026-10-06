using Finance.Domain.Categories;
using SharedKernel;

namespace Finance.Domain.Transactions;

/// <summary>
/// One category line of a split transaction: a single movement (one bank line, one total) whose amount is spread over
/// several categories, e.g. one energy bill for electricity and gas. Owned by <see cref="Transaction"/>; the lines
/// always add up exactly to the transaction's amount, in its original currency and in EUR.
/// </summary>
public sealed class TransactionSplit
{
    private TransactionSplit() { }

    public Guid Id { get; private set; }
    public int Position { get; private set; }
    public Guid CategoryId { get; private set; }
    public decimal OriginalAmount { get; private set; }

    /// <summary>EUR, derived from the transaction's rate; the rounding remainder sits on the last line.</summary>
    public decimal BaseAmount { get; private set; }

    /// <summary>Expenses only: the transaction's explicit nature, else the category's default nature.</summary>
    public ExpenseNature? Nature { get; private set; }
    public string? Note { get; private set; }

    internal static TransactionSplit Create(int position, SplitLine line, decimal baseAmount, ExpenseNature? nature) => new()
    {
        Id = Guid.CreateVersion7(),
        Position = position,
        CategoryId = line.CategoryId,
        OriginalAmount = line.Amount,
        BaseAmount = baseAmount,
        Nature = nature,
        Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
    };
}

/// <summary>
/// A requested split line. <see cref="Nature"/> is resolved by the application (explicit transaction nature, else the
/// category default) before it reaches the domain; when absent the domain uses the transaction's nature or Variable.
/// </summary>
public sealed record SplitLine(Guid CategoryId, decimal Amount, string? Note = null, ExpenseNature? Nature = null);

public static class SplitRules
{
    public const int MinLines = 2;
    public const int MaxLines = 20;
    public const int MaxNoteLength = 120;

    public static readonly Error TooFewOrTooMany = Error.Validation("Transaction.Splits",
        $"A split needs {MinLines} to {MaxLines} category lines.");

    public static readonly Error NotAllowedForType = Error.Validation("Transaction.Splits",
        "Only expenses and income can be split by category.");

    public static readonly Error NotAllowedForSource = Error.Validation("Transaction.Splits",
        "Broker records and estimated interest cannot be split.");

    public static readonly Error CategoryAndSplits = Error.Validation("Transaction.Splits",
        "Give either one category or split lines, not both.");

    public static readonly Error LineAmount = Error.Validation("Transaction.Splits",
        "Each split line needs an amount greater than zero with at most 4 decimals.");

    public static readonly Error DuplicateCategory = Error.Validation("Transaction.Splits",
        "Each category can appear only once in a split.");

    public static readonly Error NoteTooLong = Error.Validation("Transaction.Splits",
        $"Split notes are at most {MaxNoteLength} characters.");

    public static Error SumMismatch(decimal sum, decimal total) => Error.Validation("Transaction.Splits",
        $"The split lines add up to {sum:0.00##} but the transaction is {total:0.00##}.");

    /// <summary>Shape checks that need no database: count, amounts, notes, duplicates and the exact sum.</summary>
    public static Error? Validate(decimal total, IReadOnlyList<SplitLine> lines)
    {
        if (lines.Count is < MinLines or > MaxLines)
        {
            return TooFewOrTooMany;
        }

        if (lines.Any(l => l.Amount <= 0 || decimal.Round(l.Amount, 4) != l.Amount))
        {
            return LineAmount;
        }

        if (lines.Any(l => l.Note is { } n && n.Trim().Length > MaxNoteLength))
        {
            return NoteTooLong;
        }

        if (lines.Select(l => l.CategoryId).Distinct().Count() != lines.Count)
        {
            return DuplicateCategory;
        }

        var sum = lines.Sum(l => l.Amount);
        return sum == total ? null : SumMismatch(sum, total);
    }

    /// <summary>
    /// EUR amount of each line: every line is converted with the transaction's rate and rounded like the transaction,
    /// and the last line takes whatever is left so the lines add up exactly to the transaction's EUR amount.
    /// </summary>
    public static IReadOnlyList<decimal> BaseAmounts(IReadOnlyList<decimal> amounts, decimal fxRate, decimal baseTotal,
        bool isBase)
    {
        if (isBase)
        {
            return amounts;
        }

        var result = amounts.Take(amounts.Count - 1).Select(a => decimal.Round(a * fxRate, 4)).ToList();
        result.Add(baseTotal - result.Sum());
        return result;
    }

    /// <summary>
    /// Rescales lines to a new total (a recurring item confirmed with a different amount): proportional, rounded to
    /// cents, with the remainder on the last line. Returns the lines unchanged when the total did not change.
    /// </summary>
    public static IReadOnlyList<SplitLine> Rescale(IReadOnlyList<SplitLine> lines, decimal oldTotal, decimal newTotal)
    {
        if (lines.Count == 0 || oldTotal == newTotal || oldTotal <= 0)
        {
            return lines;
        }

        var scaled = lines.Take(lines.Count - 1)
            .Select(l => l with { Amount = decimal.Round(l.Amount * newTotal / oldTotal, 2, MidpointRounding.AwayFromZero) })
            .ToList();
        scaled.Add(lines[^1] with { Amount = newTotal - scaled.Sum(l => l.Amount) });
        return scaled;
    }
}
