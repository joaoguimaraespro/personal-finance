using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Finance.Domain;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class ExportTests(ApiFactory factory)
{
    private const string Hostile = "=HYPERLINK(\"https://evil.example\",\"click\")";

    private static async Task<Guid> SeedAsync(ApiClient api, int year)
    {
        var bank = await api.CreateAsync("/api/accounts", new { name = "Export bank", kind = "Bank", currency = "EUR", openingBalance = 0 });
        await api.CreateAsync("/api/transactions", new { type = "Income", occurredOn = $"{year}-02-25", amount = 3000m, accountId = bank, categoryId = SystemCatalog.CategoryId("salary") });
        await api.CreateAsync("/api/transactions", new { type = "Expense", occurredOn = $"{year}-02-10", amount = 42.40m, accountId = bank, categoryId = SystemCatalog.CategoryId("restaurants"), description = Hostile });
        await api.CreateAsync("/api/transactions", new { type = "InvestmentContribution", occurredOn = $"{year}-02-26", amount = 750m, accountId = bank, bucketId = SystemCatalog.BucketId("stocks-etfs") });
        return bank;
    }

    [Fact]
    public async Task Year_workbook_has_tables_charts_and_the_same_figures_as_the_app()
    {
        var api = await factory.OwnerAsync();
        await SeedAsync(api, 2032);

        var response = await api.Http.GetAsync("/api/exports/xlsx?kind=Year&year=2032");
        await ApiClient.EnsureAsync(response);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using (var wb = new XLWorkbook(new MemoryStream(bytes)))
        {
            wb.Worksheets.Select(w => w.Name).ShouldBe(
                ["Summary", "Monthly Summary", "Income", "Expenses", "Investments", "Net Worth", "Budgets", "Goals", "Transactions"]);
            var monthly = wb.Worksheet("Monthly Summary");
            monthly.Tables.Count().ShouldBe(1);
            var feb = monthly.Row(3);
            feb.Cell(1).GetString().ShouldBe("2032-02");
            feb.Cell(2).GetValue<decimal>().ShouldBe(3000m);
            feb.Cell(6).GetValue<decimal>().ShouldBe(42.40m);
            feb.Cell(7).GetValue<decimal>().ShouldBe(750m);

            var report = (await api.GetJsonAsync("/api/reports/monthly/2032-02")).GetProperty("current");
            feb.Cell(9).GetValue<decimal>().ShouldBe(report.GetProperty("netBalance").GetDecimal());

            // Hostile text is stored as text: never as a formula.
            var cell = wb.Worksheet("Expenses").CellsUsed().First(c => c.GetString() == Hostile);
            cell.HasFormula.ShouldBeFalse();
        }

        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        package.WorkbookPart!.WorksheetParts.SelectMany(w => w.DrawingsPart?.ChartParts ?? []).Count().ShouldBe(2);
        // Excel is stricter than most readers: the package must be schema-valid or Excel offers to "repair" it.
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package)
            .Select(e => $"{e.Part?.Uri}: {e.Description}").ToList();
        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Investments")]
    [InlineData("Portfolio")]
    [InlineData("Month")]
    public async Task Every_workbook_kind_is_schema_valid(string kind)
    {
        var api = await factory.OwnerAsync();
        await SeedAsync(api, 2035);

        var bytes = await api.Http.GetByteArrayAsync($"/api/exports/xlsx?kind={kind}&period=2035-02");

        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package)
            .Select(e => $"{e.Part?.Uri}: {e.Description}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Csv_neutralises_formula_injection()
    {
        var api = await factory.OwnerAsync();
        await SeedAsync(api, 2033);

        var csv = await api.Http.GetStringAsync("/api/exports/csv?dataset=Transactions&from=2033-02-01&to=2033-02-28");

        csv.ShouldContain("'=HYPERLINK");
        csv.Split("\r\n")[0].ShouldStartWith("date,type,category");
        var pt = await api.Http.GetStringAsync("/api/exports/csv?dataset=Monthly&from=2033-02-01&to=2033-02-28&dialect=excel-pt");
        pt.ShouldContain("3000,00");
        pt.ShouldContain(";");
    }

    [Fact]
    public async Task Json_export_round_trips_into_a_fresh_instance()
    {
        var api = await factory.OwnerAsync();
        var bank = await SeedAsync(api, 2034);
        // One energy bill split over two categories survives the round trip with its lines.
        await api.CreateAsync("/api/transactions", new
        {
            type = "Expense", occurredOn = "2034-02-14", amount = 90m, accountId = bank, description = "Energy 2034",
            splits = new object[]
            {
                new { categoryId = SystemCatalog.CategoryId("electricity"), amount = 55.55m, note = "power" },
                new { categoryId = SystemCatalog.CategoryId("groceries"), amount = 34.45m },
            },
        });
        var export = await api.Http.GetByteArrayAsync("/api/exports/json");
        var json = JsonDocument.Parse(export).RootElement;
        json.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        var archived = json.GetProperty("finance").GetProperty("transactions").EnumerateArray()
            .Single(t => t.TryGetProperty("description", out var d) && d.GetString() == "Energy 2034");
        archived.GetProperty("splits").GetArrayLength().ShouldBe(2);

        // Importing into the same instance is idempotent.
        var again = await UploadAsync(api, export);
        again.GetProperty("transactions").GetInt32().ShouldBe(0);

        // A brand-new server with its own database gets the same figures.
        await using var fresh = new ApiFactory();
        await fresh.InitializeAsync();
        var freshApi = await fresh.OwnerAsync();
        var imported = await UploadAsync(freshApi, export);
        imported.GetProperty("transactions").GetInt32()
            .ShouldBe(json.GetProperty("finance").GetProperty("transactions").GetArrayLength());

        var original = (await api.GetJsonAsync("/api/reports/annual/2034")).GetProperty("totals");
        var restored = (await freshApi.GetJsonAsync("/api/reports/annual/2034")).GetProperty("totals");
        foreach (var field in new[] { "income", "totalExpenses", "invested", "saved", "netBalance" })
        {
            restored.GetProperty(field).GetDecimal().ShouldBe(original.GetProperty(field).GetDecimal(), field);
        }

        static async Task<Dictionary<string, decimal>> ByCategory(ApiClient client) =>
            (await client.GetAsync<JsonElement[]>("/api/reports/categories/2034-02"))
            .ToDictionary(l => l.GetProperty("key").GetString()!, l => l.GetProperty("actual").GetDecimal());
        var restoredLines = await ByCategory(freshApi);
        restoredLines["electricity"].ShouldBe(55.55m);
        restoredLines["groceries"].ShouldBe(34.45m);
        restoredLines.ShouldBe(await ByCategory(api), ignoreOrder: true);
        var page = await freshApi.GetJsonAsync("/api/transactions?search=Energy%202034");
        var restoredSplit = page.GetProperty("items")[0];
        restoredSplit.GetProperty("categoryId").ValueKind.ShouldBe(JsonValueKind.Null);
        restoredSplit.GetProperty("splits").EnumerateArray().Select(l => l.GetProperty("note").ValueKind == JsonValueKind.String ? l.GetProperty("note").GetString() : null)
            .ShouldBe(["power", null]);
    }

    [Fact]
    public async Task Json_import_rejects_foreign_files()
    {
        var api = await factory.OwnerAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("{\"hello\":1}")), "file", "x.json");
        var response = await api.Http.PostAsync("/api/imports/json", content);

        ((int)response.StatusCode).ShouldBe(400);
    }

    private static async Task<JsonElement> UploadAsync(ApiClient api, byte[] export)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(export);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(file, "file", "export.json");
        var response = await api.Http.PostAsync("/api/imports/json", content);
        await ApiClient.EnsureAsync(response);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
    }
}
