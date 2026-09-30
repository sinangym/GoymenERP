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
    public void Preview_follows_workbook_order_instead_of_a_physical_sheet1_name()
    {
        var workbook = WorkbookPackage(
            Sheet(["Eski Kod", "Ürün Adı", "Grup Kimliği"],
                ["OLD-FIRST", "Gerçek ilk sayfa", Group.ToString("D")]),
            Sheet(["Yanlış", "Fiziksel", "Sayfa"]));

        var result = Reader().Read(workbook, Mapping, EmptyCodes(), Groups());

        Assert.True(result.Ready);
        Assert.Equal("OLD-FIRST", Assert.Single(result.Rows).LegacyCode);
    }

    [Fact]
    public void Preview_reads_compact_shared_strings_and_concatenates_rich_text_without_losing_whitespace()
    {
        var sheet = """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
              <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" t="s"><v>4</v></c><c r="C2" t="s"><v>5</v></c></row>
            </sheetData></worksheet>
            """;
        var sharedStrings = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="6" uniqueCount="6"><si><t>Eski Kod</t></si><si><r><t>Ürün</t></r><r><t xml:space="preserve"> Adı</t></r></si><si><t>Grup Kimliği</t></si><si><t>OLD-SHARED-1</t></si><si><r><t xml:space="preserve">  Galvaniz</t></r><r><t xml:space="preserve"> Rulo  </t></r></si><si><t>{{Group:D}}</t></si></sst>
            """;

        var result = Reader().Read(WorkbookPackage(sheet, sharedStrings: sharedStrings), Mapping, EmptyCodes(), Groups());

        Assert.True(result.Ready);
        var row = Assert.Single(result.Rows);
        Assert.Equal("OLD-SHARED-1", row.LegacyCode);
        Assert.Equal("Galvaniz Rulo", row.Name);
        Assert.Equal(Group, row.GroupId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Preview_rejects_formula_cells_even_when_the_workbook_caches_a_text_value()
    {
        var sheet = """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>Eski Kod</t></is></c><c r="B1" t="inlineStr"><is><t>Ürün Adı</t></is></c><c r="C1" t="inlineStr"><is><t>Grup Kimliği</t></is></c></row>
              <row r="2"><c r="A2" t="str"><f>&quot;OLD-FORMULA&quot;</f><v>OLD-FORMULA</v></c><c r="B2" t="inlineStr"><is><t>Formüllü</t></is></c><c r="C2" t="inlineStr"><is><t>82000000-0000-0000-0000-000000000001</t></is></c></row>
            </sheetData></worksheet>
            """;

        var result = Reader().Read(WorkbookPackage(sheet), Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Single(result.Errors, error => error.Code == ProductImportCodesV1.InvalidWorkbook);
    }

    [Fact]
    public void Preview_never_treats_style_dependent_numeric_cells_as_lossless_legacy_codes()
    {
        var sheet = """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>Eski Kod</t></is></c><c r="B1" t="inlineStr"><is><t>Ürün Adı</t></is></c><c r="C1" t="inlineStr"><is><t>Grup Kimliği</t></is></c></row>
              <row r="2"><c r="A2"><v>00123</v></c><c r="B2" t="inlineStr"><is><t>Sıfırlı kod</t></is></c><c r="C2" t="inlineStr"><is><t>82000000-0000-0000-0000-000000000001</t></is></c></row>
              <row r="3"><c r="A3"><v>1E+05</v></c><c r="B3" t="inlineStr"><is><t>Bilimsel kod</t></is></c><c r="C3" t="inlineStr"><is><t>82000000-0000-0000-0000-000000000001</t></is></c></row>
            </sheetData></worksheet>
            """;

        var result = Reader().Read(WorkbookPackage(sheet), Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Empty(result.Rows);
        Assert.Equal(new int?[] { 2, 3 }, result.Errors
            .Where(error => error.Code == ProductImportCodesV1.InvalidLegacyCode)
            .Select(error => error.RowNumber)
            .ToArray());
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
    public void Preview_stops_at_the_first_row_above_the_limit_and_returns_only_the_canonical_rejection()
    {
        var rows = Enumerable.Range(1, ProductImportApiV1.MaximumRows + 1)
            .Select(index => (IReadOnlyList<string>)[
                $"OLD-{index:D5}", $"Ürün {index}", Group.ToString("D")])
            .ToArray();

        var result = Reader().Read(Workbook(["Eski Kod", "Ürün Adı", "Grup Kimliği"], rows),
            Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Equal(ProductImportApiV1.MaximumRows + 1, result.SourceRowCount);
        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal((null, "workbook", ProductImportCodesV1.TooManyRows),
            (error.RowNumber, error.Field, error.Code));
    }

    [Fact]
    public void Row_limit_rejection_precedes_missing_required_columns_and_keeps_the_canonical_shape()
    {
        var rows = Enumerable.Range(1, ProductImportApiV1.MaximumRows + 1)
            .Select(index => (IReadOnlyList<string>)[$"OLD-{index:D5}", $"Ürün {index}"])
            .ToArray();

        var result = Reader().Read(Workbook(["Kod", "Ad"], rows), Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Equal(ProductImportApiV1.MaximumRows + 1, result.SourceRowCount);
        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Equal((null, "workbook", ProductImportCodesV1.TooManyRows),
            (error.RowNumber, error.Field, error.Code));
    }

    [Fact]
    public void Preview_refuses_a_shared_string_reference_beyond_the_bounded_import_table()
    {
        const int firstOutOfRangeIndex = (ProductImportApiV1.MaximumRows + 1) * 3;
        var shared = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        for (var index = 0; index <= firstOutOfRangeIndex; index++) shared.Append("<si><t>x</t></si>");
        shared.Append("</sst>");
        var sheet = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>Eski Kod</t></is></c><c r="B1" t="inlineStr"><is><t>Ürün Adı</t></is></c><c r="C1" t="inlineStr"><is><t>Grup Kimliği</t></is></c></row>
              <row r="2"><c r="A2" t="s"><v>{{firstOutOfRangeIndex}}</v></c><c r="B2" t="inlineStr"><is><t>Ürün</t></is></c><c r="C2" t="inlineStr"><is><t>{{Group:D}}</t></is></c></row>
            </sheetData></worksheet>
            """;

        var result = Reader().Read(WorkbookPackage(sheet, sharedStrings: shared.ToString()),
            Mapping, EmptyCodes(), Groups());

        Assert.False(result.Ready);
        Assert.Empty(result.Rows);
        Assert.Single(result.Errors, error => error.Code == ProductImportCodesV1.InvalidWorkbook);
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
        => WorkbookPackage(Sheet(header, rows));

    private static string Sheet(IReadOnlyList<string> header, params IReadOnlyList<string>[] rows)
    {
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
        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static byte[] WorkbookPackage(
        string firstLogicalSheet,
        string? physicalSheet1Decoy = null,
        string? sharedStrings = null)
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
                  <Override PartName="/xl/worksheets/import.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(archive, "xl/workbook.xml", physicalSheet1Decoy is null ? """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Ürünler" sheetId="1" r:id="rIdImport"/></sheets></workbook>
                """ : """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="İlk" sheetId="2" r:id="rIdImport"/><sheet name="Fiziksel sheet1" sheetId="1" r:id="rIdDecoy"/></sheets></workbook>
                """);
            Write(archive, "xl/_rels/workbook.xml.rels", physicalSheet1Decoy is null ? """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImport" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/import.xml"/>
                </Relationships>
                """ : """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDecoy" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rIdImport" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/import.xml"/>
                </Relationships>
                """);
            Write(archive, "xl/worksheets/import.xml", firstLogicalSheet);
            if (physicalSheet1Decoy is not null) Write(archive, "xl/worksheets/sheet1.xml", physicalSheet1Decoy);
            if (sharedStrings is not null) Write(archive, "xl/sharedStrings.xml", sharedStrings);
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
