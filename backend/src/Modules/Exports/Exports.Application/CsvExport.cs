using System.Globalization;
using System.Text;

namespace Exports.Application;

public enum CsvDataset
{
    Transactions = 0,
    Monthly = 1,
    Positions = 2,
    Dividends = 3,
    Trades = 4,
    NetWorth = 5,
}

/// <summary>
/// RFC 4180 CSV, UTF-8 with BOM (Excel-friendly). "Excel PT" dialect uses ';' and decimal commas.
/// Cells that a spreadsheet would treat as a formula (= + - @ tab CR) are prefixed with an apostrophe.
/// </summary>
public sealed class CsvExport(ExportData data)
{
    public async Task<byte[]> BuildAsync(CsvDataset dataset, DateOnly from, DateOnly to, bool excelPt, CancellationToken ct)
    {
        var culture = excelPt ? CultureInfo.GetCultureInfo("pt-PT") : CultureInfo.InvariantCulture;
        var separator = excelPt ? ';' : ',';
        var (header, rows) = dataset switch
        {
            CsvDataset.Transactions => (new[] { "date", "type", "category", "nature", "account", "to_account", "bucket", "description", "amount", "currency", "fx_rate", "amount_eur", "source", "notes" },
                (await data.TransactionsAsync(from, to, null, ct)).Select(t => new object?[] { t.Date, t.Type, t.Category, t.Nature, t.Account, t.CounterAccount, t.Bucket, t.Description, t.Amount, t.Currency, t.FxRate, t.AmountEur, t.Source, t.Notes })),
            CsvDataset.Monthly => (new[] { "month", "income", "expense_budget", "fixed_expenses", "variable_expenses", "total_expenses", "invested", "saved", "net_balance", "savings_rate" },
                (await data.MonthsAsync(from, to, ct)).Select(m => new object?[] { m.Period.ToString(), m.Income, m.ExpenseBudget, m.FixedExpenses, m.VariableExpenses, m.TotalExpenses, m.Invested, m.Saved, m.NetBalance, m.SavingsRate })),
            CsvDataset.Positions => (new[] { "security", "ticker", "isin", "asset_class", "quantity", "average_price", "current_price", "currency", "market_value_eur", "pnl_eur", "weight" },
                (await data.PositionsAsync(ct)).Select(p => new object?[] { p.Name, p.Symbol, p.Isin, p.AssetClass.ToString(), p.Quantity, p.AveragePrice, p.LastPrice, p.Currency, p.MarketValueBase, p.UnrealizedPnlBase, p.PortfolioWeight })),
            CsvDataset.Dividends => (new[] { "paid_on", "ticker", "security", "broker", "gross", "withholding_tax", "net", "currency", "net_eur" },
                (await data.DividendsAsync(from, to, ct)).Items.Select(d => new object?[] { d.PaidOn, d.Symbol, d.Name, d.Broker.ToString(), d.Gross, d.WithholdingTax, d.Net, d.Currency, d.NetBase })),
            CsvDataset.Trades => (new[] { "executed_utc", "account", "ticker", "side", "quantity", "price", "currency", "costs_eur", "cash_impact_eur", "realized_pnl_eur", "source" },
                (await data.TradesAsync(ct)).Select(t => new object?[] { t.At.UtcDateTime.ToString("s", CultureInfo.InvariantCulture), t.Account, t.Symbol, t.Side, t.Quantity, t.Price, t.Currency, t.Costs, t.AmountEur, t.RealizedEur, t.Source })),
            _ => (new[] { "date", "assets", "liabilities", "net_worth" },
                (await data.NetWorthAsync(ct)).Select(n => new object?[] { n.Date, n.Assets, n.Liabilities, n.NetWorth })),
        };

        var sb = new StringBuilder();
        sb.AppendJoin(separator, header).Append("\r\n");
        foreach (var row in rows)
        {
            sb.AppendJoin(separator, row.Select(v => Cell(v, culture, separator))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    internal static string Cell(object? value, CultureInfo culture, char separator)
    {
        var text = value switch
        {
            null => "",
            decimal d => d.ToString(culture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, culture),
            var o => o.ToString() ?? "",
        };

        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text; // CSV/formula injection guard
        }

        return text.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }
}
