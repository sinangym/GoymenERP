using System.IO.Compression;
using System.Xml.Linq;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// S5 API-34 (SAT-D236, SAT-D248): the host's xlsx composer over a SENTETIK export answer, with the shipped resource texts — one
/// header row of the wide column set, one row per invoice line with the invoice cells repeated, every order of a multi-order
/// invoice in its order cell, exact amounts as number cells only when Excel's double keeps every digit (else the exact text),
/// the same bytes for the same answer, and no workbook at all when a text is missing or an invoice has no lines. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesInvoiceExportWorkbookComposerTests
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void The_workbook_has_one_header_row_and_one_row_per_line_with_every_order_of_a_multi_order_invoice()
    {
        var (content, fileName) = Composer().Compose(Export());

        Assert.Equal("satis-faturalari-2026-11-01-2026-11-30.xlsx", fileName);
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml"],
            archive.Entries.Select(entry => entry.FullName).ToArray());
        var workbook = XDocument.Load(archive.GetEntry("xl/workbook.xml")!.Open());
        Assert.Equal("Satış Faturaları", workbook.Descendants(Spreadsheet + "sheet").Single().Attribute("name")!.Value);

        var rows = Rows(archive);
        Assert.Equal(3, rows.Count);
        Assert.Equal(SalesInvoiceExportWorkbookComposer.ColumnCount, rows[0].Count);
        Assert.Contains("Sipariş No(lar)", rows[0].Values.Select(cell => cell.Value));
        Assert.Contains("İskonto %", rows[0].Values.Select(cell => cell.Value));
        var ordersColumn = rows[0].Single(cell => cell.Value.Value == "Sipariş No(lar)").Key;
        Assert.Equal(("SS-2026-000001, SS-2026-000002", false), rows[1][ordersColumn]);
        Assert.Equal(rows[1][ordersColumn], rows[2][ordersColumn]);
        // The invoice cells repeat on every line row; the line cells differ.
        Assert.Equal(("SF-2026-000001", false), rows[1]["A"]);
        Assert.Equal(("SF-2026-000001", false), rows[2]["A"]);
        Assert.Contains(("SS-2026-000001", false), rows[1].Values);
        Assert.Contains(("SS-2026-000002", false), rows[2].Values);
        Assert.Contains(("1. taksit 2026-12-03: 24.00", false), rows[1].Values);
    }

    [Fact]
    public void The_official_vat_and_withholding_columns_sit_beside_the_computed_ones_filled_from_the_match_and_empty_without_it()
    {
        // SAT-D279 (EK55): the official document's own VAT and withholding (TL, SAT-D241) are separate
        // columns beside the computed TL VAT and withheld VAT; the computed figures stay.
        var export = Export();
        var functional = new SalesInvoiceAmountsView("10.00", "2.00", "0.40", "12.00", "11.60");
        var matched = export.Invoices[0] with
        {
            FunctionalTotals = functional, OfficialVatFunctionalAmount = "2.01", OfficialWithholdingFunctionalAmount = "0.41",
        };

        var (content, _) = Composer().Compose(export with { Invoices = [matched] });
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var rows = Rows(archive);
        var header = rows[0].ToDictionary(cell => cell.Value.Value, cell => cell.Key);
        var computedVat = header["Fatura KDV (TL)"];
        var officialVat = header["Resmî Belge KDV (TL)"];
        var computedWithheld = header["Fatura Tevkif Edilen KDV (TL)"];
        var officialWithheld = header["Resmî Belge Tevkif Edilen KDV (TL)"];
        Assert.Equal(Next(computedVat), officialVat);
        Assert.Equal(Next(officialVat), computedWithheld);
        Assert.Equal(Next(computedWithheld), officialWithheld);
        Assert.Equal(SalesInvoiceExportWorkbookComposer.ColumnCount, rows[0].Count);
        foreach (var row in rows.Skip(1))
        {
            // Invoice-level official amounts repeat on every line row; never a per-line figure.
            Assert.Equal(("2.00", true), row[computedVat]);
            Assert.Equal(("2.01", true), row[officialVat]);
            Assert.Equal(("0.40", true), row[computedWithheld]);
            Assert.Equal(("0.41", true), row[officialWithheld]);
        }

        var (unmatchedContent, _) = Composer().Compose(export with
        {
            Invoices = [matched with { OfficialVatFunctionalAmount = null, OfficialWithholdingFunctionalAmount = null }],
        });
        using var unmatchedArchive = new ZipArchive(new MemoryStream(unmatchedContent), ZipArchiveMode.Read);
        foreach (var row in Rows(unmatchedArchive).Skip(1))
        {
            Assert.Equal(("2.00", true), row[computedVat]);
            Assert.False(row.ContainsKey(officialVat));
            Assert.Equal(("0.40", true), row[computedWithheld]);
            Assert.False(row.ContainsKey(officialWithheld));
        }

        static string Next(string column) => SalesInvoiceExportWorkbookComposer.ColumnName(
            Enumerable.Range(0, SalesInvoiceExportWorkbookComposer.ColumnCount)
                .Single(index => SalesInvoiceExportWorkbookComposer.ColumnName(index) == column) + 1);
    }

    [Fact]
    public void Exact_amounts_are_number_cells_and_a_value_beyond_fifteen_digits_stays_exact_text()
    {
        var export = Export();
        var invoice = export.Invoices[0];
        var line = invoice.Lines[0] with { Quantity = "1234567890.123456", NetUnitPrice = "10.50", DiscountPercent = "2.5" };
        var (content, _) = Composer().Compose(export with { Invoices = [invoice with { Lines = [line] }] });

        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var row = Rows(archive)[1];
        Assert.Contains(("1234567890.123456", false), row.Values);
        Assert.Contains(("10.50", true), row.Values);
        Assert.Contains(("2.5", true), row.Values);
        Assert.Contains(("12.00", true), row.Values);
        // A date and an id are text, never a serial number.
        Assert.Contains(("2026-11-02", false), row.Values);
        Assert.Contains((line.ProductId.ToString("D"), false), row.Values);
    }

    [Fact]
    public void The_same_answer_composes_the_same_bytes_and_an_empty_range_is_the_header_row_only()
    {
        var export = Export();
        Assert.Equal(Composer().Compose(export).Content, Composer().Compose(export).Content);

        var (content, _) = Composer().Compose(export with { Invoices = [] });
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.Single(Rows(archive));
    }

    [Fact]
    public void A_missing_text_an_invoice_without_lines_or_another_format_composes_no_workbook()
    {
        var export = Export();
        Assert.Throws<InvalidOperationException>(() => new SalesInvoiceExportWorkbookComposer(new MissingText(Texts(), "ColumnSalesOrderNos"))
            .Compose(export));
        Assert.Throws<InvalidOperationException>(() => Composer().Compose(export with { Invoices = [export.Invoices[0] with { Lines = [] }] }));
        Assert.Throws<InvalidOperationException>(() => Composer().Compose(export with { Format = "ubl-tr" }));
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    public void Column_letters_follow_the_a1_notation(int index, string expected) =>
        Assert.Equal(expected, SalesInvoiceExportWorkbookComposer.ColumnName(index));

    private static SalesInvoiceExportWorkbookComposer Composer() => new(Texts());

    private static IStringLocalizer<SalesInvoiceExportWorkbookComposer> Texts() => new StringLocalizer<SalesInvoiceExportWorkbookComposer>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
            NullLoggerFactory.Instance));

    private static List<Dictionary<string, (string Value, bool Numeric)>> Rows(ZipArchive archive)
    {
        var sheet = XDocument.Load(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return [.. sheet.Descendants(Spreadsheet + "row").Select(row => row.Elements(Spreadsheet + "c").ToDictionary(
            cell => new string([.. cell.Attribute("r")!.Value.TakeWhile(char.IsLetter)]),
            cell => cell.Attribute("t")?.Value == "inlineStr"
                ? (cell.Descendants(Spreadsheet + "t").Single().Value, false)
                : (cell.Element(Spreadsheet + "v")!.Value, true)))];
    }

    private static SalesInvoiceExportV1 Export()
    {
        var amounts = new SalesInvoiceAmountsView("10.00", "2.00", "0.00", "12.00", "12.00");
        var firstOrder = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var secondOrder = Guid.Parse("10000000-0000-4000-8000-000000000002");
        var product = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var unit = Guid.Parse("30000000-0000-4000-8000-000000000001");
        SalesInvoiceExportLineV1 Line(int lineNo, Guid order, string orderNo) => new(lineNo, $"IRS-2026-00000{lineNo}", 1,
            new DateOnly(2026, 11, 2), order, orderNo, product, unit, "1.000", "10.00", null, null, null, null, "20", null, amounts, null);
        var invoice = new SalesInvoiceExportInvoiceV1(Guid.Parse("40000000-0000-4000-8000-000000000001"), "SF-2026-000001", null, null,
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), Guid.Parse("50000000-0000-4000-8000-000000000001"),
            Guid.Parse("60000000-0000-4000-8000-000000000001"), Guid.Parse("70000000-0000-4000-8000-000000000001"), 2, null, amounts, null,
            null, null, [new SalesInvoiceFinalInstalmentView(1, new DateOnly(2026, 12, 3), "24.00", null, Guid.Empty, Guid.Empty)],
            ["SS-2026-000001", "SS-2026-000002"], [Line(1, firstOrder, "SS-2026-000001"), Line(2, secondOrder, "SS-2026-000002")]);
        return new SalesInvoiceExportV1("xlsx", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), [invoice]);
    }

    private sealed class MissingText(IStringLocalizer<SalesInvoiceExportWorkbookComposer> real, string key)
        : IStringLocalizer<SalesInvoiceExportWorkbookComposer>
    {
        public LocalizedString this[string name] => name == key ? new(name, name, resourceNotFound: true) : real[name];

        public LocalizedString this[string name, params object[] arguments] => real[name, arguments];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => real.GetAllStrings(includeParentCultures);
    }
}
