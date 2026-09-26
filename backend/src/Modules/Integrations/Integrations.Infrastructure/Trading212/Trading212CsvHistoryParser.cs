using Integrations.Application.Contracts;
using Integrations.Application.Sync;

namespace Integrations.Infrastructure.Trading212;

internal sealed class Trading212CsvHistoryParser : ICsvHistoryParser
{
    public BrokerKind Kind => BrokerKind.Trading212;

    public async Task<CsvImportData> ParseAsync(Stream csv, string accountCurrency, CancellationToken ct)
    {
        using var reader = new StreamReader(csv);
        var text = await reader.ReadToEndAsync(ct);
        var result = Trading212CsvParser.Parse(new StringReader(text), accountCurrency);
        return new CsvImportData(result.Trades, result.Cash, result.Dividends, result.Warnings);
    }
}
