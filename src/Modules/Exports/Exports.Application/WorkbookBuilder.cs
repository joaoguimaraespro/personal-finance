using ClosedXML.Excel;
using Finance.Domain.Transactions;

namespace Exports.Application;

public enum ExportKind
{
    Month = 0,
    Year = 1,
    Period = 2,
    All = 3,
    Expenses = 4,
    Income = 5,
    Investments = 6,
    Portfolio = 7,
}

public sealed record ExportRequest(ExportKind Kind, DateOnly From, DateOnly To);

/// <summary>
/// Builds a real .xlsx: one Excel table per sheet (filters, banding, frozen header), typed numbers and dates,
/// money/percent formats and native charts. Text is written as text — nothing is ever written as a formula.
/// </summary>
public sealed class WorkbookBuilder(ExportData data)
{
    private const string Money = "#,##0.00";
    private const string Percent = "0.0%";
    private const string Date = "yyyy-mm-dd";
    private const string Quantity = "#,##0.######";

    private sealed record Column(string Header, string? Format = null, double Width = 14);

    public async Task<byte[]> BuildAsync(ExportRequest request, CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Title = "Personal Finance export";
        wb.Properties.Author = "personal-finance";
        var charts = new List<ChartSpec>();
        var k = request.Kind;
        bool Has(params ExportKind[] kinds) => kinds.Contains(k);

        if (Has(ExportKind.Month, ExportKind.Year, ExportKind.Period, ExportKind.All))
        {
            await SummaryAsync(wb, request, ct);
        }

        if (Has(ExportKind.Month, ExportKind.Year, ExportKind.Period, ExportKind.All, ExportKind.Expenses, ExportKind.Income))
        {
            charts.AddRange(await MonthlyAsync(wb, request, ct));
        }

        if (Has(ExportKind.Month, ExportKind.Year, ExportKind.Period, ExportKind.All, ExportKind.Income))
        {
            await TransactionsSheetAsync(wb, "Income", request, [TransactionType.Income], ct);
        }

        if (Has(ExportKind.Month, ExportKind.Year, ExportKind.Period, ExportKind.All, ExportKind.Expenses))
        {
            await TransactionsSheetAsync(wb, "Expenses", request, [TransactionType.Expense], ct);
        }

        if (Has(ExportKind.Year, ExportKind.Period, ExportKind.All, ExportKind.Investments))
        {
            await TransactionsSheetAsync(wb, "Investments", request,
                [TransactionType.InvestmentContribution, TransactionType.Savings], ct);
        }

        if (Has(ExportKind.All, ExportKind.Investments, ExportKind.Portfolio))
        {
            await PortfolioAsync(wb, ct);
            await PositionsAsync(wb, ct);
        }

        if (Has(ExportKind.All, ExportKind.Investments))
        {
            await DividendsAsync(wb, request, ct);
            await TradesAsync(wb, ct);
            charts.AddRange(await PerformanceAsync(wb, request, ct));
        }

        if (Has(ExportKind.All, ExportKind.Year, ExportKind.Portfolio))
        {
            charts.AddRange(await NetWorthAsync(wb, ct));
        }

        if (Has(ExportKind.Month, ExportKind.Year, ExportKind.Period, ExportKind.All))
        {
            await BudgetsAsync(wb, ct);
            await GoalsAsync(wb, ct);
            await TransactionsSheetAsync(wb, "Transactions", request, null, ct);
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        var validCharts = charts.Where(c => wb.TryGetWorksheet(c.Sheet, out var ws) && ws.LastRowUsed()?.RowNumber() > 1).ToList();
        ExcelCharts.Add(stream, validCharts);
        return stream.ToArray();
    }

    private async Task SummaryAsync(XLWorkbook wb, ExportRequest r, CancellationToken ct)
    {
        var months = await data.MonthsAsync(r.From, r.To, ct);
        var income = months.Sum(m => m.Income);
        var expenses = months.Sum(m => m.TotalExpenses);
        var invested = months.Sum(m => m.Invested);
        var saved = months.Sum(m => m.Saved);
        var rows = new List<object?[]>
        {
            new object?[] { "Period", $"{r.From:yyyy-MM-dd} → {r.To:yyyy-MM-dd}" },
            new object?[] { "Income", income },
            new object?[] { "Fixed expenses", months.Sum(m => m.FixedExpenses) },
            new object?[] { "Variable expenses", months.Sum(m => m.VariableExpenses) },
            new object?[] { "Total expenses", expenses },
            new object?[] { "Invested", invested },
            new object?[] { "Saved", saved },
            new object?[] { "Net balance", income - expenses },
            new object?[] { "Savings rate", income == 0 ? null : (invested + saved) / income },
        };
        var portfolio = await data.PortfolioAsync(ct);
        if (portfolio.Accounts.Count > 0)
        {
            rows.Add(["Portfolio value (today)", portfolio.TotalValue]);
            rows.Add(["Portfolio total return", portfolio.TotalReturn]);
        }

        var ws = Table(wb, "Summary", [new("Metric", Width: 28), new("Value", Money, 22)], rows);
        ws.Cell(10, 2).Style.NumberFormat.Format = Percent; // savings rate row
        ws.Cell(2, 2).Style.NumberFormat.Format = "@";
    }

    private async Task<IEnumerable<ChartSpec>> MonthlyAsync(XLWorkbook wb, ExportRequest r, CancellationToken ct)
    {
        var months = await data.MonthsAsync(r.From, r.To, ct);
        var cumInvested = 0m;
        var cumSaved = 0m;
        var rows = months.Select(m =>
        {
            cumInvested += m.Invested;
            cumSaved += m.Saved;
            return new object?[]
            {
                m.Period.ToString(), m.Income, m.ExpenseBudget, m.FixedExpenses, m.VariableExpenses, m.TotalExpenses,
                m.Invested, m.Saved, m.NetBalance, m.SavingsRate, cumInvested, cumSaved, m.InvestmentRate, m.SavingsOnlyRate,
                m.Status.ToString(),
            };
        }).ToList();
        Table(wb, "Monthly Summary",
        [
            new("Month", Width: 10), new("Income", Money), new("Expense budget", Money), new("Fixed expenses", Money),
            new("Variable expenses", Money), new("Total expenses", Money), new("Invested", Money), new("Saved", Money),
            new("Net balance", Money), new("Savings rate", Percent), new("Cumulative invested", Money),
            new("Cumulative saved", Money), new("% invested", Percent), new("% saved", Percent), new("Status", Width: 10),
        ], rows);
        var last = rows.Count + 1;
        return
        [
            new ChartSpec("Monthly Summary", "Income vs expenses", ChartKind.Bar, $"'Monthly Summary'!$A$2:$A${last}",
                [("Income", $"'Monthly Summary'!$B$2:$B${last}", "16A34A"), ("Total expenses", $"'Monthly Summary'!$F$2:$F${last}", "E11D48")],
                (0, last + 1, 8, last + 20)),
            new ChartSpec("Monthly Summary", "Cumulative invested and saved", ChartKind.Line, $"'Monthly Summary'!$A$2:$A${last}",
                [("Cumulative invested", $"'Monthly Summary'!$K$2:$K${last}", "7C3AED"), ("Cumulative saved", $"'Monthly Summary'!$L$2:$L${last}", "0891B2")],
                (8, last + 1, 15, last + 20)),
        ];
    }

    private async Task TransactionsSheetAsync(XLWorkbook wb, string name, ExportRequest r, TransactionType[]? types,
        CancellationToken ct)
    {
        var rows = (await data.TransactionsAsync(r.From, r.To, types, ct)).Select(t => new object?[]
        {
            t.Date, t.Type, t.Category, t.Nature, t.Account, t.CounterAccount, t.Bucket, t.Description, t.Amount,
            t.Currency, t.FxRate, t.AmountEur, t.Source, t.Notes,
        });
        Table(wb, name,
        [
            new("Date", Date, 12), new("Type", Width: 14), new("Category", Width: 18), new("Nature", Width: 10),
            new("Account", Width: 18), new("To account", Width: 18), new("Bucket", Width: 16), new("Description", Width: 32),
            new("Amount", Money), new("Currency", Width: 9), new("FX rate", "0.000000", 11), new("Amount (EUR)", Money),
            new("Source", Width: 12), new("Notes", Width: 30),
        ], rows);
    }

    private async Task PortfolioAsync(XLWorkbook wb, CancellationToken ct)
    {
        var s = await data.PortfolioAsync(ct);
        var allocation = await data.AllocationAsync(ct);
        var rows = new List<object?[]>
        {
            new object?[] { "Total value", s.TotalValue }, new object?[] { "Market value", s.MarketValue },
            new object?[] { "Cash", s.Cash }, new object?[] { "Net contributions", s.NetContributions },
            new object?[] { "Total return", s.TotalReturn }, new object?[] { "Unrealised P&L", s.UnrealizedPnl },
            new object?[] { "Realised P&L", s.RealizedPnl }, new object?[] { "Dividends (net)", s.Dividends },
            new object?[] { "Fees & taxes", s.Fees },
        };
        rows.AddRange(allocation.Select(a => new object?[] { $"Allocation — {a.AssetClass} (actual {a.Actual:P1}, target {(a.Target is { } t ? t.ToString("P1", System.Globalization.CultureInfo.InvariantCulture) : "—")})", a.Value }));
        Table(wb, "Portfolio", [new("Metric", Width: 52), new("EUR", Money, 18)], rows);
    }

    private async Task PositionsAsync(XLWorkbook wb, CancellationToken ct)
    {
        var rows = (await data.PositionsAsync(ct)).SelectMany(p => p.Holdings.Select(h => new object?[]
        {
            p.Name, p.Symbol, p.Isin, p.AssetClass.ToString(), h.AccountName, h.Broker.ToString(), h.Quantity, h.AveragePrice,
            p.LastPrice, p.Currency, p.Quantity == 0 ? 0 : p.MarketValueBase * h.Quantity / p.Quantity,
            p.Quantity == 0 ? 0 : p.UnrealizedPnlBase * h.Quantity / p.Quantity, p.UnrealizedPnlPercent, p.PortfolioWeight,
        }));
        Table(wb, "Positions",
        [
            new("Security", Width: 34), new("Ticker", Width: 9), new("ISIN", Width: 14), new("Asset class", Width: 11),
            new("Account", Width: 18), new("Broker", Width: 14), new("Quantity", Quantity), new("Average price", Money),
            new("Current price", Money), new("Currency", Width: 9), new("Market value (EUR)", Money, 17),
            new("P&L (EUR)", Money), new("P&L %", Percent, 9), new("Portfolio %", Percent, 11),
        ], rows);
    }

    private async Task DividendsAsync(XLWorkbook wb, ExportRequest r, CancellationToken ct)
    {
        var d = await data.DividendsAsync(null, r.To, ct);
        Table(wb, "Dividends",
        [
            new("Paid on", Date, 12), new("Ticker", Width: 9), new("Security", Width: 30), new("Broker", Width: 14),
            new("Gross", Money), new("Withholding tax", Money, 15), new("Net", Money), new("Currency", Width: 9),
            new("Net (EUR)", Money), new("Withholding inferred", Width: 12),
        ], d.Items.Select(i => new object?[]
        {
            i.PaidOn, i.Symbol, i.Name, i.Broker.ToString(), i.Gross, i.WithholdingTax, i.Net, i.Currency, i.NetBase,
            i.WithholdingDerived ? "yes" : "no",
        }));
    }

    private async Task TradesAsync(XLWorkbook wb, CancellationToken ct) =>
        Table(wb, "Trades",
        [
            new("Executed (UTC)", "yyyy-mm-dd hh:mm", 17), new("Account", Width: 18), new("Ticker", Width: 9),
            new("Side", Width: 7), new("Quantity", Quantity), new("Price", Money), new("Currency", Width: 9),
            new("Fees & taxes (EUR)", Money, 16), new("Cash impact (EUR)", Money, 16), new("Realised P&L (EUR)", Money, 17),
            new("Source", Width: 14),
        ], (await data.TradesAsync(ct)).Select(t => new object?[]
        {
            t.At.UtcDateTime, t.Account, t.Symbol, t.Side, t.Quantity, t.Price, t.Currency, t.Costs, t.AmountEur, t.RealizedEur, t.Source,
        }));

    private async Task<IEnumerable<ChartSpec>> PerformanceAsync(XLWorkbook wb, ExportRequest r, CancellationToken ct)
    {
        var p = await data.PerformanceAsync(null, r.To, ct);
        var rows = p.Series.Select(s => new object?[] { s.Date, s.Value, s.NetContributions }).ToList();
        var ws = Table(wb, "Performance", [new("Date", Date, 12), new("Portfolio value (EUR)", Money, 20), new("Net contributions (EUR)", Money, 22)], rows);
        ws.Cell(1, 5).Value = "Time-weighted return (period)";
        ws.Cell(1, 6).Value = p.TimeWeightedReturn;
        ws.Cell(2, 5).Value = "Money-weighted return (XIRR, annual)";
        ws.Cell(2, 6).Value = p.MoneyWeightedReturn;
        ws.Range(1, 6, 2, 6).Style.NumberFormat.Format = Percent;
        ws.Column(5).Width = 36;
        var last = rows.Count + 1;
        return rows.Count < 2 ? [] :
        [
            new ChartSpec("Performance", "Portfolio value vs contributions", ChartKind.Line, $"Performance!$A$2:$A${last}",
                [("Value", $"Performance!$B$2:$B${last}", "2F6FED"), ("Net contributions", $"Performance!$C$2:$C${last}", "94A3B8")],
                (4, 4, 14, 24)),
        ];
    }

    private async Task<IEnumerable<ChartSpec>> NetWorthAsync(XLWorkbook wb, CancellationToken ct)
    {
        var rows = (await data.NetWorthAsync(ct)).Select(n => new object?[] { n.Date, n.Assets, n.Liabilities, n.NetWorth }).ToList();
        Table(wb, "Net Worth", [new("Date", Date, 12), new("Assets", Money), new("Liabilities", Money), new("Net worth", Money)], rows);
        var last = rows.Count + 1;
        return rows.Count < 2 ? [] :
        [
            new ChartSpec("Net Worth", "Net worth", ChartKind.Line, $"'Net Worth'!$A$2:$A${last}",
                [("Net worth", $"'Net Worth'!$D$2:$D${last}", "2F6FED")], (5, 1, 14, 20)),
        ];
    }

    private async Task BudgetsAsync(XLWorkbook wb, CancellationToken ct) =>
        Table(wb, "Budgets",
        [
            new("Effective from", Width: 13), new("Target", Width: 12), new("Mode", Width: 16), new("Value", "#,##0.00####", 12),
            new("Bucket / category", Width: 22), new("Note", Width: 30),
        ], (await data.BudgetsAsync(ct)).Select(b => new object?[] { b.EffectiveFrom, b.Target, b.Mode, b.Value, b.Name, b.Note }));

    private async Task GoalsAsync(XLWorkbook wb, CancellationToken ct) =>
        Table(wb, "Goals",
        [
            new("Goal", Width: 24), new("Target", Money), new("Current", Money), new("Progress", Percent, 10),
            new("Remaining", Money), new("Target date", Date, 12), new("Needed / month", Money, 15), new("Achieved", Width: 10),
        ], (await data.GoalsAsync(ct)).Select(g => new object?[]
        {
            g.Name, g.TargetAmount, g.CurrentAmount, g.Progress, g.Remaining, g.TargetDate, g.MonthlyNeeded, g.Achieved ? "yes" : "no",
        }));

    private static IXLWorksheet Table(XLWorkbook wb, string name, IReadOnlyList<Column> columns, IEnumerable<object?[]> rows)
    {
        var ws = wb.AddWorksheet(name);
        for (var c = 0; c < columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = columns[c].Header;
            ws.Column(c + 1).Width = columns[c].Width;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length && c < columns.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                cell.Value = row[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    int i => i,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    DateTime dt => dt,
                    bool b => b,
                    // Plain text only: a description starting with "=" stays text, it is never a formula.
                    var other => other.ToString(),
                };
                if (columns[c].Format is { } format)
                {
                    cell.Style.NumberFormat.Format = format;
                }
            }

            r++;
        }

        var range = ws.Range(1, 1, Math.Max(2, r - 1), columns.Count);
        var table = range.CreateTable(new string([.. name.Where(char.IsLetterOrDigit)]));
        table.Theme = XLTableTheme.TableStyleLight9;
        table.ShowAutoFilter = true;
        ws.SheetView.FreezeRows(1);
        return ws;
    }
}
