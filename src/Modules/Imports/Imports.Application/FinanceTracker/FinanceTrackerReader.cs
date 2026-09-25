using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Imports.Application.FinanceTracker;

/// <summary>
/// Reads the "Gestor Financeiro Pessoal" workbook layout (see docs/excel-migration.md). Only input cells and cached
/// summary values are read; formulas are never evaluated and no cell content is treated as an instruction.
/// </summary>
public static class FinanceTrackerReader
{
    public static readonly string[] MonthSheets = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");
    private const int MaxTextLength = 200;

    public static (RawWorkbook? Workbook, string? Error) Read(Stream stream)
    {
        var guard = WorkbookGuard.Check(stream);
        if (guard is not null)
        {
            return (null, guard);
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream, new LoadOptions { RecalculateAllFormulas = false });
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException
                                       or NotSupportedException or FormatException)
        {
            return (null, "The file could not be read as an Excel workbook.");
        }

        using (workbook)
        {
            var layoutError = CheckLayout(workbook);
            if (layoutError is not null)
            {
                return (null, layoutError);
            }

            var warnings = new List<string>();
            var rows = new List<RawRow>();
            for (var m = 0; m < 12; m++)
            {
                rows.AddRange(ReadMonth(workbook.Worksheet(MonthSheets[m]), m + 1, warnings));
            }

            var config = ReadConfig(workbook.Worksheets.First(w => Normalize(w.Name).Contains("configura")));
            var summary = workbook.Worksheets.FirstOrDefault(w => Normalize(w.Name).Contains("resumo anual"));
            var expected = summary is null ? [] : ReadExpected(summary);
            return (new RawWorkbook(rows, config, expected, warnings), null);
        }
    }

    private static string? CheckLayout(XLWorkbook wb)
    {
        var missing = MonthSheets.Where(s => !wb.TryGetWorksheet(s, out _)).ToList();
        if (missing.Count > 0)
        {
            return $"Unsupported workbook: missing month sheets {string.Join(", ", missing)}.";
        }

        if (!wb.Worksheets.Any(w => Normalize(w.Name).Contains("configura")))
        {
            return "Unsupported workbook: the configuration sheet was not found.";
        }

        foreach (var name in MonthSheets)
        {
            var ws = wb.Worksheet(name);
            if (Normalize(Text(ws.Cell("A3"))) != "rendimento" ||
                Normalize(Text(ws.Cell("A18"))) != "despesas fixas" ||
                Normalize(Text(ws.Cell("A33"))) != "despesas variaveis")
            {
                return $"Unsupported layout in sheet '{name}': expected the Finance Tracker template.";
            }
        }

        return null;
    }

    private static IEnumerable<RawRow> ReadMonth(IXLWorksheet ws, int month, List<string> warnings)
    {
        // Income: B5 salary, B6 other income; notes in column C.
        foreach (var (row, label) in new[] { (5, "salary"), (6, "other-income") })
        {
            var amount = Amount(ws.Cell(row, 2), ws.Name, warnings);
            if (amount > 0)
            {
                yield return new RawRow(month, RawBlock.Income, $"B{row}", label, null, Text(ws.Cell(row, 1)), amount,
                    null, null, Text(ws.Cell(row, 3)), null);
            }
        }

        // Allocation actuals D11–D14 with their "Estado" in F.
        foreach (var (row, bucket) in new[] { (11, "stocks-etfs"), (12, "crypto"), (13, "travel-fund"), (14, "other-savings") })
        {
            var amount = Amount(ws.Cell(row, 4), ws.Name, warnings);
            var status = Text(ws.Cell(row, 6));
            if (amount > 0 || status is not null && Normalize(status) != "n/a")
            {
                yield return new RawRow(month, RawBlock.Allocation, $"D{row}", bucket, null, Text(ws.Cell(row, 1)),
                    amount, null, null, null, status);
            }
        }

        // Fixed expenses rows 20–30: A description, B category, C amount, D debit date/day.
        for (var row = 20; row <= 30; row++)
        {
            var amount = Amount(ws.Cell(row, 3), ws.Name, warnings);
            if (amount > 0)
            {
                var (date, day) = DateOrDay(ws.Cell(row, 4));
                yield return new RawRow(month, RawBlock.Fixed, $"C{row}", Text(ws.Cell(row, 1)),
                    Text(ws.Cell(row, 2)), Text(ws.Cell(row, 1)), amount, date, day, null, null);
            }
        }

        // Variable expenses rows 35–46: A category, B description, C amount, D date.
        for (var row = 35; row <= 46; row++)
        {
            var amount = Amount(ws.Cell(row, 3), ws.Name, warnings);
            if (amount > 0)
            {
                var (date, day) = DateOrDay(ws.Cell(row, 4));
                yield return new RawRow(month, RawBlock.Variable, $"C{row}", Text(ws.Cell(row, 1)),
                    Text(ws.Cell(row, 1)), Text(ws.Cell(row, 2)), amount, date, day, null, null);
            }
        }
    }

    private static RawConfig ReadConfig(IXLWorksheet ws)
    {
        var categories = new List<string>();
        for (var row = 17; row <= 40; row++)
        {
            if (Text(ws.Cell(row, 2)) is { } label)
            {
                categories.Add(label);
            }
        }

        return new RawConfig(Percent(ws.Cell("B5")), Percent(ws.Cell("B6")), Percent(ws.Cell("B9")),
            Percent(ws.Cell("B10")), categories);
    }

    private static List<SheetTotals> ReadExpected(IXLWorksheet ws)
    {
        var result = new List<SheetTotals>();
        for (var m = 0; m < 12; m++)
        {
            var row = 4 + m;
            result.Add(new SheetTotals(m + 1, Cached(ws.Cell(row, 2)), Cached(ws.Cell(row, 4)), Cached(ws.Cell(row, 5)),
                Cached(ws.Cell(row, 7)), Cached(ws.Cell(row, 8))));
        }

        return result;
    }

    private static decimal Amount(IXLCell cell, string sheet, List<string> warnings)
    {
        if (cell.HasFormula)
        {
            return 0; // Input cells never hold formulas in the template; ignore rather than evaluate.
        }

        var value = cell.Value;
        if (value.IsBlank)
        {
            return 0;
        }

        if (value.IsNumber)
        {
            var d = (decimal)value.GetNumber();
            if (d < 0)
            {
                warnings.Add($"{sheet}!{cell.Address}: negative amount ignored.");
                return 0;
            }

            return decimal.Round(d, 4);
        }

        if (value.IsText && decimal.TryParse(value.GetText().Replace("€", "", StringComparison.Ordinal).Trim(),
                NumberStyles.Number, Pt, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        warnings.Add($"{sheet}!{cell.Address}: non-numeric amount ignored.");
        return 0;
    }

    private static decimal? Percent(IXLCell cell) =>
        cell.Value.IsNumber ? decimal.Round((decimal)cell.Value.GetNumber(), 6) : null;

    private static decimal? Cached(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        return value.IsNumber ? decimal.Round((decimal)value.GetNumber(), 4) : null;
    }

    private static (DateOnly? Date, int? Day) DateOrDay(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsDateTime)
        {
            return (DateOnly.FromDateTime(value.GetDateTime()), null);
        }

        if (value.IsNumber)
        {
            var n = value.GetNumber();
            return n is >= 1 and <= 31 ? (null, (int)n) : (null, null);
        }

        if (value.IsText)
        {
            var text = value.GetText().Trim();
            if (DateTime.TryParse(text, Pt, DateTimeStyles.None, out var dt))
            {
                return (DateOnly.FromDateTime(dt), null);
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31)
            {
                return (null, day);
            }
        }

        return (null, null);
    }

    /// <summary>Plain text, stripped of control characters and length-capped. Never evaluated.</summary>
    internal static string? Text(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        if (value.IsBlank)
        {
            return null;
        }

        var raw = value.IsText ? value.GetText() : value.ToString(CultureInfo.InvariantCulture);
        var clean = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxTextLength)
        {
            clean = clean[..MaxTextLength];
        }

        return clean.Length == 0 ? null : clean;
    }

    public static string Normalize(string? text)
    {
        if (text is null)
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark &&
                (char.IsLetterOrDigit(c) || c is ' ' or '/'))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
