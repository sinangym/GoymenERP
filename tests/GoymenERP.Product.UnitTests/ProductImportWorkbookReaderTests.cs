using System.IO.Compression;
using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductImportWorkbookReaderTests
{
    private static readonly Guid Group = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private static readonly ProductImportColumnMappingV1 Mapping = new("Eski Kod", "Ürün Adı", "Grup Kimliği");

    [Fact]
    public void Preview_reads_the_first_sheet_and_binds_the_exact_file_and_mapping()
    {
        var workbook = Workbook(
            ["Eski Kod", "Ürün Adı", "Grup Kimliği"],
            ["OLD-001", "  Galvaniz Rulo  ", Group.ToString("D")],
            ["OLD-002", "Boyalı Rulo", Group.ToString("D")]);

        var result = Reader().Read(workbook, Mapping, EmptyCodes(), Groups());
        var replay = Reader().Read(workbook, Mapping, EmptyCodes(), Groups());

        Assert.True(result.Ready);
        Assert.Equal(2, result.SourceRowCount);
        Assert.Equal(["OLD-001", "OLD-002"], result.Rows.Select(row => row.LegacyCode));
        Assert.Equal("Galvaniz Rulo", result.Rows[0].Name);
        Assert.Equal(result.FileSha256, replay.FileSha256);
        Assert.Equal(result.PreviewToken, replay.PreviewToken);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Preview_rejects_repeated_and_already_registered_legacy_codes_without_updating_them()
    {
        var workbook = Workbook(
            ["Eski Kod", "Ürün Adı", "Grup Kimliği"],
            ["OLD-001", "Bir", Group.ToString("D")],
            ["OLD-001", "İki", Group.ToString("D")],
            ["OLD-EXISTING", "Üç", Group.ToString("D")]);

        var result = Reader().Read(workbook, Mapping,
            new HashSet<string>(["OLD-EXISTING"], StringComparer.Ordinal), Groups());

        Assert.False(result.Ready);
        Assert.Single(result.Rows);
        Assert.Contains(result.Errors, error => error.RowNumber == 3 && error.Code == ProductImportCodesV1.RepeatedLegacyCode);
        Assert.Contains(result.Errors, error => error.RowNumber == 4 && error.Code == ProductImportCodesV1.ExistingLegacyCode);
    }

    [Fact]
    public void Preview_reports_all_row_errors_and_never_partially_marks_the_file_ready()
    {
        var workbook = Workbook(
            ["Eski Kod", "Ürün Adı", "Grup Kimliği"],
            ["", "", Guid.NewGuid().ToString("D")],
            ["OLD-002", "Geçerli", "not-a-guid"]);

        var result = Reader().Read(workbook, Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Equal(2, result.SourceRowCount);
        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, error => error.RowNumber == 2 && error.Field == "legacyCode");
        Assert.Contains(result.Errors, error => error.RowNumber == 2 && error.Field == "name");
        Assert.Equal(2, result.Errors.Count(error => error.Field == "groupId"));
    }

    [Fact]
    public void Preview_rejects_missing_mapping_columns_and_malformed_files()
    {
        var missing = Reader().Read(Workbook(["Kod", "Ad"], ["A", "B"]), Mapping, EmptyCodes(), Groups());
        var malformed = Reader().Read(Encoding.UTF8.GetBytes("not an xlsx"), Mapping, EmptyCodes(), Groups());

        Assert.False(missing.Ready);
        Assert.Contains(missing.Errors, error => error.Code == ProductImportCodesV1.RequiredColumnMissing);
        Assert.False(malformed.Ready);
        Assert.Single(malformed.Errors, error => error.Code == ProductImportCodesV1.InvalidWorkbook);
    }

    [Fact]
    public void Downloadable_error_csv_is_utf8_bom_and_stably_escaped()
    {
        var bytes = ProductImportErrorCsvV1.Write([
            new(4, "name", "invalid_name", "Virgül, ve \"tırnak\""),
            new(2, "legacyCode", "required", "Kod zorunlu"),
        ]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var csv = Encoding.UTF8.GetString(bytes.AsSpan(3));
        Assert.StartsWith("row,field,code,message\r\n2,", csv, StringComparison.Ordinal);
        Assert.Contains("\"Virgül, ve \"\"tırnak\"\"\"", csv, StringComparison.Ordinal);
    }

    private static ProductImportWorkbookReader Reader() => new();
    private static HashSet<string> EmptyCodes() => new(StringComparer.Ordinal);
    private static HashSet<Guid> Groups() => [Group];

    private static byte[] Workbook(IReadOnlyList<string> header, params IReadOnlyList<string>[] rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheets><sheet name="Ürünler" sheetId="1"/></sheets></workbook>
                """);
            var all = new[] { header }.Concat(rows).ToArray();
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var row = 0; row < all.Length; row++)
            {
                xml.Append("<row r=\"").Append(row + 1).Append("\">");
                for (var column = 0; column < all[row].Count; column++)
                    xml.Append("<c r=\"").Append(Column(column + 1)).Append(row + 1).Append("\" t=\"inlineStr\"><is><t>")
                        .Append(System.Security.SecurityElement.Escape(all[row][column])).Append("</t></is></c>");
                xml.Append("</row>");
            }
            xml.Append("</sheetData></worksheet>");
            Write(archive, "xl/worksheets/sheet1.xml", xml.ToString());
        }
        return output.ToArray();
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Column(int index)
    {
        var value = new StringBuilder();
        while (index > 0)
        {
            index--;
            value.Insert(0, (char)('A' + index % 26));
            index /= 26;
        }
        return value.ToString();
    }
}
