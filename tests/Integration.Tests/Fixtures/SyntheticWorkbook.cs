using ClosedXML.Excel;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Builds a workbook with the exact layout of the original "Gestor Financeiro Pessoal" template but filled with
/// fictitious numbers. Real personal spreadsheets never enter the repository.
/// </summary>
public static class SyntheticWorkbook
{
    private static readonly string[] Months = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];

    private static readonly (string Desc, string Category)[] FixedRows =
    [
        ("Renda / Prestação", "Habitação"), ("Eletricidade", "Eletricidade"), ("Água / Gás", "Água / Gás"),
        ("Internet", "Internet"), ("Telemóvel", "Telemóvel"), ("Seguro de Saúde", "Seguro de Saúde"),
        ("Seguro Automóvel", "Seguro Automóvel"), ("Ginásio", "Ginásio"),
        ("Streaming / Subscrições", "Streaming / Subscrições"), ("Outra Fixa 1", "Outro"), ("Outra Fixa 2", "Outro"),
    ];

    private static readonly string[] VariableRows =
    [
        "Supermercado", "Restaurantes", "Transportes", "Lazer / Entretenimento", "Roupa", "Saúde / Farmácia",
        "Educação", "Prendas", "Viagens", "Extra 1", "Extra 2", "Extra 3",
    ];

    public sealed record MonthData(decimal Salary, decimal Other, decimal Stocks, decimal Crypto, decimal Travel,
        decimal OtherSavings, decimal[] Fixed, decimal[] Variable, string Status = "Feito");

    /// <summary>Months 1–3 filled, the rest empty — like a year in progress.</summary>
    public static IReadOnlyList<MonthData> DefaultData() =>
    [
        new(2000m, 150m, 500m, 0m, 100m, 50m,
            [650m, 45.30m, 30m, 35m, 12.99m, 0m, 0m, 30m, 10.99m, 0m, 0m],
            [320.45m, 85.20m, 60m, 40m, 0m, 12.50m, 0m, 0m, 0m, 0m, 0m, 0m]),
        new(2000m, 0m, 500m, 25m, 100m, 50m,
            [650m, 52.10m, 28m, 35m, 12.99m, 0m, 0m, 30m, 10.99m, 0m, 0m],
            [298.10m, 120m, 55m, 0m, 79.99m, 0m, 0m, 25m, 0m, 15m, 0m, 0m]),
        new(2100m, 300m, 525m, 0m, 105m, 52.50m,
            [650m, 48.75m, 31m, 35m, 12.99m, 0m, 120m, 30m, 10.99m, 0m, 0m],
            [305m, 64.40m, 62m, 30m, 0m, 0m, 49m, 0m, 0m, 0m, 0m, 0m], "Parcial"),
    ];

    public static MemoryStream Build(IReadOnlyList<MonthData>? data = null, string? hostileDescription = null)
    {
        data ??= DefaultData();
        using var wb = new XLWorkbook();
        wb.AddWorksheet("📊 Dashboard").Cell("A1").Value = "📊 Gestor Financeiro Pessoal — Dashboard";
        var summary = wb.AddWorksheet("📅 Resumo Anual");
        summary.Cell("A1").Value = "📅 Resumo Financeiro Anual";

        for (var m = 0; m < 12; m++)
        {
            var ws = wb.AddWorksheet(Months[m]);
            ws.Cell("A3").Value = "RENDIMENTO";
            ws.Cell("A5").Value = "Salário";
            ws.Cell("A6").Value = "Outros Rendimentos";
            ws.Cell("A11").Value = "Ações / ETFs";
            ws.Cell("A12").Value = "Crypto";
            ws.Cell("A13").Value = "Férias";
            ws.Cell("A14").Value = "Outras Poupanças";
            ws.Cell("A18").Value = "DESPESAS FIXAS";
            ws.Cell("A33").Value = "DESPESAS VARIÁVEIS";
            for (var i = 0; i < FixedRows.Length; i++)
            {
                ws.Cell(20 + i, 1).Value = FixedRows[i].Desc;
                ws.Cell(20 + i, 2).Value = FixedRows[i].Category;
            }

            for (var i = 0; i < VariableRows.Length; i++)
            {
                ws.Cell(35 + i, 1).Value = VariableRows[i];
            }

            for (var r = 11; r <= 14; r++)
            {
                ws.Cell(r, 6).Value = "N/A";
            }

            if (m >= data.Count)
            {
                continue;
            }

            var d = data[m];
            ws.Cell("B5").Value = d.Salary;
            ws.Cell("B6").Value = d.Other;
            ws.Cell("C5").Value = "Salário líquido";
            ws.Cell("D11").Value = d.Stocks;
            ws.Cell("D12").Value = d.Crypto;
            ws.Cell("D13").Value = d.Travel;
            ws.Cell("D14").Value = d.OtherSavings;
            for (var r = 11; r <= 14; r++)
            {
                ws.Cell(r, 6).Value = d.Status;
            }

            for (var i = 0; i < d.Fixed.Length; i++)
            {
                ws.Cell(20 + i, 3).Value = d.Fixed[i];
                if (d.Fixed[i] > 0)
                {
                    ws.Cell(20 + i, 4).Value = 5 + i; // "Data Débito" as a day of month
                }
            }

            for (var i = 0; i < d.Variable.Length; i++)
            {
                ws.Cell(35 + i, 3).Value = d.Variable[i];
            }

            if (m == 0 && hostileDescription is not null)
            {
                ws.Cell("B36").Value = hostileDescription;
            }

            // Resumo Anual stores what the workbook itself would show; the importer reconciles against it.
            var row = 4 + m;
            summary.Cell(row, 2).Value = d.Salary + d.Other;
            summary.Cell(row, 4).Value = d.Fixed.Sum();
            summary.Cell(row, 5).Value = d.Variable.Sum();
            summary.Cell(row, 7).Value = d.Stocks + d.Crypto;
            summary.Cell(row, 8).Value = d.Travel + d.OtherSavings;
        }

        var config = wb.AddWorksheet("⚙️ Configurações");
        config.Cell("B5").Value = 0.25;
        config.Cell("B6").Value = 0;
        config.Cell("B9").Value = 0.05;
        config.Cell("B10").Value = 0.05;
        var categories = FixedRows.Select(f => f.Category).Concat(VariableRows.Take(9)).Distinct().ToList();
        for (var i = 0; i < categories.Count; i++)
        {
            config.Cell(17 + i, 2).Value = categories[i];
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
