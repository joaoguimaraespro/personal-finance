using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Integrations.Infrastructure.Ibkr;

internal sealed record FlexStatement(
    string BaseCurrency,
    DateOnly? ToDate,
    decimal? EndingCash,
    decimal? EndingValue,
    IReadOnlyList<XElement> OpenPositions,
    IReadOnlyList<XElement> Trades,
    IReadOnlyList<XElement> CashTransactions,
    IReadOnlyList<XElement> DailyEquity);

internal sealed record FlexStatus(string Status, string? ReferenceCode, int? ErrorCode, string? ErrorMessage);

/// <summary>Parses Flex Web Service XML. DTDs and external entities are disabled; attributes are read as data.</summary>
internal static class FlexStatementParser
{
    private static readonly XmlReaderSettings Safe = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        MaxCharactersInDocument = 200_000_000,
    };

    public static XDocument Load(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Safe);
        return XDocument.Load(reader);
    }

    /// <summary>SendRequest/GetStatement status envelope, or null when the document is an actual statement.</summary>
    public static FlexStatus? Status(XDocument doc)
    {
        var root = doc.Root;
        if (root?.Name.LocalName != "FlexStatementResponse")
        {
            return null;
        }

        return new FlexStatus(
            root.Element("Status")?.Value ?? "Fail",
            root.Element("ReferenceCode")?.Value,
            int.TryParse(root.Element("ErrorCode")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null,
            root.Element("ErrorMessage")?.Value);
    }

    public static IReadOnlyList<FlexStatement> Statements(XDocument doc) =>
        doc.Descendants("FlexStatement").Select(s =>
        {
            var baseCurrency = s.Element("AccountInformation")?.Attribute("currency")?.Value ?? "EUR";
            var cashSummary = s.Element("CashReport")?.Elements("CashReportCurrency")
                .FirstOrDefault(c => c.Attribute("currency")?.Value == "BASE_SUMMARY");
            var nav = s.Element("ChangeInNAV");
            var daily = s.Element("EquitySummaryInBase")?.Elements("EquitySummaryByReportDateInBase").ToList() ?? [];
            return new FlexStatement(
                baseCurrency,
                Date(Attr(s, "toDate")),
                Dec(cashSummary, "endingCash") ?? Dec(daily.LastOrDefault(), "cash"),
                Dec(nav, "endingValue") ?? Dec(daily.LastOrDefault(), "total"),
                Section(s, "OpenPositions", "OpenPosition")
                    .Where(p => Attr(p, "levelOfDetail") is null or "SUMMARY").ToList(),
                Section(s, "Trades", "Trade")
                    .Where(t => Attr(t, "levelOfDetail") is null or "EXECUTION").ToList(),
                Section(s, "CashTransactions", "CashTransaction")
                    .Where(c => Attr(c, "levelOfDetail") is null or "DETAIL").ToList(),
                daily);
        }).ToList();

    private static IEnumerable<XElement> Section(XElement statement, string section, string item) =>
        statement.Element(section)?.Elements(item) ?? [];

    public static string? Attr(XElement? e, string name) =>
        e?.Attribute(name)?.Value is { Length: > 0 } v ? v[..Math.Min(v.Length, 200)] : null;

    public static decimal? Dec(XElement? e, string name) =>
        decimal.TryParse(Attr(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static readonly string[] DateFormats = ["yyyyMMdd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy"];
    private static readonly string[] DateTimeFormats =
        ["yyyyMMdd;HHmmss", "yyyyMMdd HHmmss", "yyyy-MM-dd;HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyyMMdd", "yyyy-MM-dd"];

    public static DateOnly? Date(string? value) =>
        value is not null && DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : value is not null && value.Length >= 8 && DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)
                ? d2
                : null;

    /// <summary>IBKR reports in the account's configured zone; treated as UTC here (day-level precision is what matters).</summary>
    public static DateTimeOffset? DateTime(string? value) =>
        value is not null && System.DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero)
            : null;
}
