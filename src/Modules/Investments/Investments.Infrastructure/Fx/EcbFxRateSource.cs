using System.Globalization;
using System.Xml;
using Investments.Application.Abstractions;
using Investments.Domain;

namespace Investments.Infrastructure.Fx;

/// <summary>European Central Bank euro reference rates (public, no key). Parsed with DTDs and entities disabled.</summary>
internal sealed class EcbFxRateSource(HttpClient http) : IFxRateSource
{
    public const string Host = "www.ecb.europa.eu";

    public async Task<IReadOnlyList<FxRate>> FetchAsync(FxHistory history, CancellationToken ct)
    {
        var path = history switch
        {
            FxHistory.Latest => "/stats/eurofxref/eurofxref-daily.xml",
            FxHistory.Last90Days => "/stats/eurofxref/eurofxref-hist-90d.xml",
            _ => "/stats/eurofxref/eurofxref-hist.xml",
        };
        await using var stream = await http.GetStreamAsync(new Uri($"https://{Host}{path}"), ct);
        return Parse(stream);
    }

    internal static List<FxRate> Parse(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
        };
        var rates = new List<FxRate>();
        using var reader = XmlReader.Create(xml, settings);
        DateOnly? day = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Cube")
            {
                continue;
            }

            if (reader.GetAttribute("time") is { } time)
            {
                day = DateOnly.ParseExact(time, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (day is not null && reader.GetAttribute("currency") is { Length: 3 } currency &&
                     decimal.TryParse(reader.GetAttribute("rate"), NumberStyles.Float, CultureInfo.InvariantCulture,
                         out var rate) && rate > 0)
            {
                rates.Add(FxRate.Create(day.Value, currency, rate));
            }
        }

        return rates;
    }
}
