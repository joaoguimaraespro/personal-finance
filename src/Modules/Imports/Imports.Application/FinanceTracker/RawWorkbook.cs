namespace Imports.Application.FinanceTracker;

public enum RawBlock
{
    Income = 0,
    Allocation = 1,
    Fixed = 2,
    Variable = 3,
}

/// <summary>One input row read verbatim from a month sheet. Labels are untrusted text and never interpreted.</summary>
public sealed record RawRow(
    int Month,
    RawBlock Block,
    string Cell,
    string? Label,
    string? CategoryLabel,
    string? Description,
    decimal Amount,
    DateOnly? Date,
    int? Day,
    string? Notes,
    string? Status);

public sealed record RawConfig(
    decimal? StocksPercent,
    decimal? CryptoPercent,
    decimal? TravelPercent,
    decimal? OtherSavingsPercent,
    IReadOnlyList<string> Categories);

/// <summary>Cached values of "📅 Resumo Anual" rows 4–15, used only to prove the import reproduces the workbook.</summary>
public sealed record SheetTotals(int Month, decimal? Income, decimal? Fixed, decimal? Variable, decimal? Invested,
    decimal? Saved);

public sealed record RawWorkbook(
    IReadOnlyList<RawRow> Rows,
    RawConfig Config,
    IReadOnlyList<SheetTotals> Expected,
    IReadOnlyList<string> Warnings);
