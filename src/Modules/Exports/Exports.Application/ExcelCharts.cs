using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace Exports.Application;

public enum ChartKind
{
    Bar = 0,
    Line = 1,
}

/// <summary>A native Excel chart bound to cell ranges (so it updates if the user edits the table).</summary>
public sealed record ChartSpec(
    string Sheet,
    string Title,
    ChartKind Kind,
    string CategoriesRef,
    IReadOnlyList<(string Name, string ValuesRef, string Color)> Series,
    (int FromCol, int FromRow, int ToCol, int ToRow) Anchor);

/// <summary>ClosedXML has no chart support; charts are added with the Open XML SDK after the workbook is written.</summary>
internal static class ExcelCharts
{
    public static void Add(Stream xlsx, IReadOnlyList<ChartSpec> charts)
    {
        if (charts.Count == 0)
        {
            return;
        }

        using var document = SpreadsheetDocument.Open(xlsx, isEditable: true);
        var workbookPart = document.WorkbookPart!;
        foreach (var group in charts.GroupBy(c => c.Sheet))
        {
            var sheet = workbookPart.Workbook!.Descendants<Sheet>().First(s => s.Name == group.Key);
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var drawingsPart = worksheetPart.DrawingsPart ?? worksheetPart.AddNewPart<DrawingsPart>();
            if (drawingsPart.WorksheetDrawing is null)
            {
                drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
                // CT_Worksheet is an ordered sequence: <drawing> must precede <legacyDrawing>, <tableParts> and <extLst>.
                var drawing = new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) };
                var worksheet = worksheetPart.Worksheet!;
                OpenXmlElement? successor = worksheet.GetFirstChild<LegacyDrawing>() ?? (OpenXmlElement?)worksheet.GetFirstChild<LegacyDrawingHeaderFooter>()
                    ?? worksheet.GetFirstChild<Picture>() ?? (OpenXmlElement?)worksheet.GetFirstChild<OleObjects>()
                    ?? worksheet.GetFirstChild<Controls>() ?? (OpenXmlElement?)worksheet.GetFirstChild<WebPublishItems>()
                    ?? worksheet.GetFirstChild<TableParts>() ?? (OpenXmlElement?)worksheet.GetFirstChild<WorksheetExtensionList>();
                if (successor is null)
                {
                    worksheet.Append(drawing);
                }
                else
                {
                    worksheet.InsertBefore(drawing, successor);
                }
            }

            uint id = 2;
            foreach (var spec in group)
            {
                var chartPart = drawingsPart.AddNewPart<ChartPart>();
                chartPart.ChartSpace = BuildChartSpace(spec);
                chartPart.ChartSpace.Save();
                drawingsPart.WorksheetDrawing.Append(Anchor(spec, drawingsPart.GetIdOfPart(chartPart), id++));
            }

            drawingsPart.WorksheetDrawing.Save();
            worksheetPart.Worksheet!.Save();
        }
    }

    private static C.ChartSpace BuildChartSpace(ChartSpec spec)
    {
        const uint catAxisId = 48650112, valAxisId = 48672768;
        OpenXmlCompositeElement plot = spec.Kind == ChartKind.Bar
            ? new C.BarChart(new C.BarDirection { Val = C.BarDirectionValues.Column },
                new C.BarGrouping { Val = C.BarGroupingValues.Clustered }, new C.VaryColors { Val = false })
            : new C.LineChart(new C.Grouping { Val = C.GroupingValues.Standard }, new C.VaryColors { Val = false });

        uint index = 0;
        foreach (var (name, valuesRef, color) in spec.Series)
        {
            var fill = new C.ChartShapeProperties(spec.Kind == ChartKind.Bar
                ? new A.SolidFill(new A.RgbColorModelHex { Val = color })
                : new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = color })) { Width = 28575 });
            OpenXmlCompositeElement series = spec.Kind == ChartKind.Bar ? new C.BarChartSeries() : new C.LineChartSeries();
            series.Append(new C.Index { Val = index }, new C.Order { Val = index },
                new C.SeriesText(new C.NumericValue(name)), fill);
            if (spec.Kind == ChartKind.Line)
            {
                series.Append(new C.Marker(new C.Symbol { Val = C.MarkerStyleValues.None }));
            }

            series.Append(new C.CategoryAxisData(new C.StringReference(new C.Formula(spec.CategoriesRef))),
                new C.Values(new C.NumberReference(new C.Formula(valuesRef))));
            if (spec.Kind == ChartKind.Line)
            {
                series.Append(new C.Smooth { Val = false });
            }

            plot.Append(series);
            index++;
        }

        if (spec.Kind == ChartKind.Bar)
        {
            plot.Append(new C.GapWidth { Val = 80 });
        }

        plot.Append(new C.AxisId { Val = catAxisId }, new C.AxisId { Val = valAxisId });

        var chart = new C.Chart(
            new C.Title(new C.ChartText(new C.RichText(new A.BodyProperties(), new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.RunProperties { FontSize = 1200, Bold = true }, new A.Text(spec.Title))))),
                new C.Overlay { Val = false }),
            new C.AutoTitleDeleted { Val = false },
            new C.PlotArea(new C.Layout(), plot,
                new C.CategoryAxis(new C.AxisId { Val = catAxisId }, new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                    new C.Delete { Val = false }, new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                    new C.TickLabelPosition { Val = C.TickLabelPositionValues.Low }, new C.CrossingAxis { Val = valAxisId },
                    new C.Crosses { Val = C.CrossesValues.AutoZero }),
                new C.ValueAxis(new C.AxisId { Val = valAxisId }, new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                    new C.Delete { Val = false }, new C.AxisPosition { Val = C.AxisPositionValues.Left },
                    new C.MajorGridlines(new C.ChartShapeProperties(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "E2E8F0" })))),
                    new C.NumberingFormat { FormatCode = "#,##0", SourceLinked = false },
                    new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo }, new C.CrossingAxis { Val = catAxisId },
                    new C.Crosses { Val = C.CrossesValues.AutoZero })),
            new C.Legend(new C.LegendPosition { Val = C.LegendPositionValues.Bottom }, new C.Overlay { Val = false }),
            new C.PlotVisibleOnly { Val = true });

        return new C.ChartSpace(new C.EditingLanguage { Val = "en-US" }, new C.RoundedCorners { Val = false }, chart);
    }

    private static Xdr.TwoCellAnchor Anchor(ChartSpec spec, string chartRelId, uint id)
    {
        var (fromCol, fromRow, toCol, toRow) = spec.Anchor;
        return new Xdr.TwoCellAnchor(
            new Xdr.FromMarker(new Xdr.ColumnId(fromCol.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"), new Xdr.RowId(fromRow.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")),
            new Xdr.ToMarker(new Xdr.ColumnId(toCol.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"), new Xdr.RowId(toRow.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")),
            new Xdr.GraphicFrame(
                new Xdr.NonVisualGraphicFrameProperties(new Xdr.NonVisualDrawingProperties { Id = id, Name = spec.Title },
                    new Xdr.NonVisualGraphicFrameDrawingProperties()),
                new Xdr.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 }),
                new A.Graphic(new A.GraphicData(new C.ChartReference { Id = chartRelId })
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" })) { Macro = "" },
            new Xdr.ClientData());
    }
}
