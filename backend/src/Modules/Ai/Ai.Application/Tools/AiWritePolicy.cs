using Finance.Domain.Transactions;
using SharedKernel;

namespace Ai.Application.Tools;

/// <summary>
/// What AI write tools may touch (ADR-0008). Broker-sourced rows (Trading 212, IBKR, demo broker), investment
/// entries, savings movements and estimated interest belong to their integration or engine and are never writable.
/// </summary>
public static class AiWritePolicy
{
    public static readonly IReadOnlySet<DataSource> WritableSources =
        new HashSet<DataSource> { DataSource.Manual, DataSource.Recurring, DataSource.Xlsx, DataSource.Csv, DataSource.Json };

    public static readonly IReadOnlySet<TransactionType> WritableTypes =
        new HashSet<TransactionType> { TransactionType.Expense, TransactionType.Income, TransactionType.Transfer };

    public static bool IsEditable(DataSource source, TransactionType type) =>
        WritableSources.Contains(source) && WritableTypes.Contains(type);

    /// <summary>Why a transaction cannot be changed by AI, or null when it can.</summary>
    public static string? RefusalFor(DataSource source, TransactionType type) =>
        source switch
        {
            DataSource.Trading212 or DataSource.InteractiveBrokers or DataSource.Demo or DataSource.MarketData =>
                "This transaction was imported from a broker and is read-only.",
            DataSource.InterestEstimate =>
                "Estimated interest is recalculated automatically and cannot be changed.",
            _ when !WritableTypes.Contains(type) =>
                "Only hand-entered expenses, income and transfers can be changed by AI; investment and savings entries cannot.",
            _ => null,
        };
}

/// <summary>A write the domain refused (not found, broker-sourced, archived, conflicting): a clear error for the model.</summary>
public sealed class ToolRefusedException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static ToolRefusedException From(Error error) => new(error.Type switch
    {
        ErrorType.NotFound => 404,
        ErrorType.Forbidden => 403,
        ErrorType.Conflict => 409,
        _ => 400,
    }, error is ValidationError v ? string.Join(" ", v.Errors.Select(e => e.Description)) : error.Description);
}
