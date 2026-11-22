using System.IO.Compression;
using System.Security;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>WP12 / NEEDS-SINAN WP12-S2: XLSX is read only through the known headers; SENTETIK workbooks built in memory.</summary>
public sealed class FinanceBankStatementXlsxParserTests
{
    /// <summary>A cell: text (inline string), a number (raw invariant text) or empty.</summary>
    internal sealed record C(string? Text = null, string? Number = null, bool Formula = false);

    internal static byte[] Workbook(IReadOnlyList<IReadOnlyList<C?>> rows, bool date1904 = false, bool macro = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            Add("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + (date1904 ? "<workbookPr date1904=\"1\"/>" : string.Empty)
                + "<sheets><sheet name=\"Ekstre\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            if (macro) Add("xl/vbaProject.bin", "x");
            var sheet = new StringBuilder("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var r = 0; r < rows.Count; r++)
            {
                sheet.Append("<row r=\"").Append(r + 1).Append("\">");
                for (var c = 0; c < rows[r].Count; c++)
                {
                    if (rows[r][c] is not { } cell) continue;
                    var reference = (char)('A' + c) + (r + 1).ToString();
                    var formula = cell.Formula ? "<f>1+1</f>" : string.Empty;
                    sheet.Append(cell.Number is not null
                        ? $"<c r=\"{reference}\">{formula}<v>{cell.Number}</v></c>"
                        : $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(cell.Text)}</t></is></c>");
                }

                sheet.Append("</row>");
            }

            Add("xl/worksheets/sheet1.xml", sheet.Append("</sheetData></worksheet>").ToString());
        }

        return buffer.ToArray();
    }

    internal static byte[] SampleWorkbook() => Workbook([
        [new C("Tarih"), new C("Açıklama"), new C("Alacak"), new C("Borç")],
        [new C("17.11.2026"), new C("SENTETIK havale"), new C(Number: "12.5"), null],
    ]);

    private static readonly C?[] Header =
        [new("İşlem Tarihi"), new("Valör"), new("Açıklama"), new("Borç"), new("Alacak"), new("Bakiye"), new("Dekont No"), new("Para Birimi")];

    [Fact]
    public void Known_headers_after_a_banner_read_serial_and_text_dates_and_the_inflow_outflow_pair()
    {
        var file = Workbook([
            [new C("SENTETIK BANKA HESAP EKSTRESI")],
            Header,
            [new C(Number: "46343"), null, new C("Gelen havale SENTETIK"), null, new C(Number: "1234.50"), new C(Number: "-10.5"), new C("D-1"), new C("TRY")],
            [new C("18.11.2026"), new C("2026-11-19"), new C("Masraf"), new C(Number: "3.5"), new C(Number: "0"), null, null, null],
        ]);
        var result = FinanceBankStatementParser.ParseXlsx(file);
        Assert.True(result.IsValid, string.Join(",", result.Issues.Select(issue => issue.Code)));
        Assert.Equal(2, result.Lines.Count);
        var first = result.Lines[0];
        Assert.Equal((3, new DateOnly(2026, 11, 17), FinanceBankStatementDirectionV1.Inflow, "1234.50", "-10.5", "D-1", "TRY"),
            (first.LineNumber, first.TransactionOn, first.Direction, first.Amount, first.Balance, first.Reference, first.CurrencyText));
        var second = result.Lines[1];
        Assert.Equal((new DateOnly(2026, 11, 18), (DateOnly?)new DateOnly(2026, 11, 19), FinanceBankStatementDirectionV1.Outflow, "3.5"),
            (second.TransactionOn, second.ValueOn, second.Direction, second.Amount));
    }

    [Fact]
    public void Header_names_compare_case_insensitively_in_turkish()
    {
        var file = Workbook([
            [new C("TARİH"), new C("AÇIKLAMA"), new C("GİRİŞ"), new C("ÇIKIŞ")],
            [new C("17.11.2026"), new C("SENTETIK"), new C(Number: "5"), null],
        ]);
        Assert.True(FinanceBankStatementParser.ParseXlsx(file).IsValid);
    }

    [Fact]
    public void An_unknown_or_repeated_header_or_a_signed_amount_column_refuses_the_file()
    {
        C?[] row = [new C("17.11.2026"), new C("x"), new C(Number: "1"), null];
        Assert.Equal("xlsx_header_unknown", Assert.Single(FinanceBankStatementParser.ParseXlsx(Workbook([
            [new C("Tarih"), new C("Açıklama"), new C("Alacak"), new C("Borç"), new C("Kanal")], row])).Issues).Code);
        Assert.Equal("xlsx_header_unknown", Assert.Single(FinanceBankStatementParser.ParseXlsx(Workbook([
            [new C("Tarih"), new C("Açıklama"), new C("Alacak"), new C("Giriş")], row])).Issues).Code);
        Assert.Equal("xlsx_header_unknown", Assert.Single(FinanceBankStatementParser.ParseXlsx(Workbook([
            [new C("Tarih"), new C("Açıklama"), new C("Tutar")], row])).Issues).Code);
        Assert.Equal("xlsx_header_required_missing", Assert.Single(FinanceBankStatementParser.ParseXlsx(Workbook([
            [new C("Tarih"), new C("Açıklama"), new C("Alacak")], row])).Issues).Code);
        Assert.Equal("xlsx_header_unknown", Assert.Single(FinanceBankStatementParser.ParseXlsx(Workbook([[new C("Kolon")]])).Issues).Code);
    }

    [Fact]
    public void Text_amounts_negative_sides_both_sides_formulas_macros_and_1904_dates_are_refused()
    {
        C?[] head = [new C("Tarih"), new C("Açıklama"), new C("Alacak"), new C("Borç")];
        foreach (var bad in new C?[][]
                 {
                     [new C("17.11.2026"), new C("x"), new C("1.234,56"), null],
                     [new C("17.11.2026"), new C("x"), new C(Number: "-1"), null],
                     [new C("17.11.2026"), new C("x"), new C(Number: "1"), new C(Number: "2")],
                     [new C("17/11/2026"), new C("x"), new C(Number: "1"), null],
                 })
        {
            var result = FinanceBankStatementParser.ParseXlsx(Workbook([head, bad]));
            Assert.False(result.IsValid);
            Assert.Empty(result.Lines);
        }

        Assert.Contains(FinanceBankStatementParser.ParseXlsx(Workbook([head, [new C("17.11.2026"), new C("x"),
            new C(Number: "1", Formula: true), null]])).Issues, issue => issue.Code == "xlsx_formula_not_supported");
        Assert.Contains(FinanceBankStatementParser.ParseXlsx(Workbook([head], macro: true)).Issues,
            issue => issue.Code == "xlsx_active_content");
        Assert.Contains(FinanceBankStatementParser.ParseXlsx(Workbook([head], date1904: true)).Issues,
            issue => issue.Code == "xlsx_date1904_not_supported");
        Assert.Contains(FinanceBankStatementParser.ParseXlsx(Encoding.UTF8.GetBytes("not a zip")).Issues,
            issue => issue.Code == "xlsx_invalid");
    }
}
