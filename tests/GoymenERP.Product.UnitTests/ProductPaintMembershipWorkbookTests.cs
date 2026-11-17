using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;

namespace GoymenERP.Product.UnitTests;

/// <summary>Actual synthetic OOXML bytes exercise the real parser. Product existence, membership writes and SQL are owner tests.</summary>
public sealed class ProductPaintMembershipWorkbookTests
{
    private const string DefaultColumn = ProductPaintMembershipLimitsV1.DefaultProductCodeColumn;
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DocumentRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void The_user_mapped_text_column_preserves_leading_zeroes_case_and_actual_product_codes()
    {
        var bytes = Workbook(Sheet(Row(1, Text("A", "Not"), Text("B", "Boya ürün kodu")),
            Row(2, Text("A", "SENTETIK"), Text("B", " 0007 ")),
            Row(3, Text("B", "paint-001")), Row(4, Text("B", "PAINT-001"))));

        var result = ProductPaintMembershipWorkbook.Read(bytes, " Boya ürün kodu ");

        Assert.True(result.Ready);
        Assert.Equal("Boya ürün kodu", result.ProductCodeColumn);
        Assert.Equal(3, result.SourceRowCount);
        Assert.Equal(new[] { "0007", "paint-001", "PAINT-001" }, result.Rows.Select(row => row.ProductCode));
        Assert.Equal(new[] { 2, 3, 4 }, result.Rows.Select(row => row.RowNumber));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.FileSha256);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void An_omitted_mapping_uses_the_published_default_column(string? mapping)
    {
        var result = ProductPaintMembershipWorkbook.Read(Workbook(StandardSheet("SNT-001")), mapping!);

        Assert.True(result.Ready);
        Assert.Equal(DefaultColumn, result.ProductCodeColumn);
        Assert.Equal("SNT-001", Assert.Single(result.Rows).ProductCode);
    }

    [Fact]
    public void The_first_logical_worksheet_is_selected_by_its_relationship_instead_of_its_zip_name()
    {
        var bytes = Workbook(StandardSheet("REAL-001"), secondSheet: StandardSheet("DECOY-001"));

        var result = ProductPaintMembershipWorkbook.Read(bytes, DefaultColumn);

        Assert.True(result.Ready);
        Assert.Equal("REAL-001", Assert.Single(result.Rows).ProductCode);
    }

    [Fact]
    public void Shared_strings_and_rich_text_preserve_code_text_without_adding_phonetic_annotations()
    {
        var strings = $"<sst xmlns=\"{MainNamespace}\"><si><t>{Escape(DefaultColumn)}</t></si>"
            + "<si><r><t>00</t></r><r><t>07</t></r><rPh><t>SENTETIK-phonetic</t></rPh></si></sst>";
        var sheet = Sheet(Row(1, "<c r=\"A{row}\" t=\"s\"><v>0</v></c>"),
            Row(2, "<c r=\"A{row}\" t=\"s\"><v>1</v></c>"));

        var result = ProductPaintMembershipWorkbook.Read(Workbook(sheet, sharedStrings: strings), DefaultColumn);

        Assert.True(result.Ready);
        Assert.Equal("0007", Assert.Single(result.Rows).ProductCode);
    }

    [Fact]
    public void An_explicit_string_value_is_text_when_it_has_no_formula()
    {
        var sheet = Sheet(Row(1, Text("A", DefaultColumn)), Row(2, "<c r=\"A{row}\" t=\"str\"><v>0007</v></c>"));

        var result = ProductPaintMembershipWorkbook.Read(Workbook(sheet), DefaultColumn);

        Assert.True(result.Ready);
        Assert.Equal("0007", Assert.Single(result.Rows).ProductCode);
    }

    [Fact]
    public void Empty_rows_are_ignored_and_nonempty_rows_without_a_mapped_code_are_reported_at_their_actual_row()
    {
        var sheet = Sheet(Row(1, Text("A", DefaultColumn), Text("B", "Not")), Row(2),
            Row(3, Text("A", " ")), Row(4, Text("B", "SENTETIK-note")), Row(5, Text("A", "SNT-001")));

        var result = ProductPaintMembershipWorkbook.Read(Workbook(sheet), DefaultColumn);

        Assert.False(result.Ready);
        Assert.Equal(2, result.SourceRowCount);
        Assert.Equal(5, Assert.Single(result.Rows).RowNumber);
        var error = Assert.Single(result.Errors);
        Assert.Equal(4, error.RowNumber);
        Assert.Equal("productCode", error.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("n")]
    [InlineData("b")]
    [InlineData("e")]
    [InlineData("d")]
    public void A_numeric_or_other_nontext_code_is_refused_even_when_its_display_would_look_like_text(string type)
    {
        var sheet = Sheet(Row(1, Text("A", DefaultColumn)),
            Row(2, $"<c r=\"A{{row}}\" t=\"{type}\"><v>0007</v></c>"));

        var result = ProductPaintMembershipWorkbook.Read(Workbook(sheet), DefaultColumn);

        Assert.False(result.Ready);
        Assert.Empty(result.Rows);
        Assert.Equal(2, Assert.Single(result.Errors).RowNumber);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate_mapped")]
    [InlineData("duplicate_other")]
    [InlineData("case_mismatch")]
    public void A_missing_ambiguous_or_case_changed_header_is_not_silently_mapped(string fault)
    {
        var header = fault switch
        {
            "missing" => Row(1, Text("A", "Başka kod")),
            "duplicate_mapped" => Row(1, Text("A", DefaultColumn), Text("B", " " + DefaultColumn + " ")),
            "duplicate_other" => Row(1, Text("A", DefaultColumn), Text("B", "Not"), Text("C", "Not")),
            "case_mismatch" => Row(1, Text("A", "ürün kodu")),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Refused(Workbook(Sheet(header, Row(2, Text("A", "SNT-001")))));
    }

    [Fact]
    public void Duplicate_product_codes_are_compared_ordinally_after_whitespace_trimming()
    {
        var result = ProductPaintMembershipWorkbook.Read(Workbook(StandardSheet(" SNT-001 ", "SNT-001")), DefaultColumn);

        Assert.False(result.Ready);
        Assert.Equal(2, result.SourceRowCount);
        Assert.Single(result.Rows);
        Assert.Equal(3, Assert.Single(result.Errors).RowNumber);
    }

    [Theory]
    [InlineData("too_long")]
    [InlineData("control")]
    public void Invalid_code_text_is_reported_without_normalizing_it_into_an_existing_product(string fault)
    {
        var code = fault == "too_long" ? new string('A', 65) : "SNT\n001";

        var result = ProductPaintMembershipWorkbook.Read(Workbook(StandardSheet(code)), DefaultColumn);

        Assert.False(result.Ready);
        Assert.Empty(result.Rows);
        Assert.Equal(2, Assert.Single(result.Errors).RowNumber);
    }

    [Fact]
    public void Exactly_five_thousand_actual_code_rows_are_accepted()
    {
        var codes = Enumerable.Range(1, ProductPaintMembershipLimitsV1.MaximumRows)
            .Select(number => "SNT-" + number.ToString(CultureInfo.InvariantCulture)).ToArray();

        var result = ProductPaintMembershipWorkbook.Read(Workbook(StandardSheet(codes)), DefaultColumn);

        Assert.True(result.Ready);
        Assert.Equal(ProductPaintMembershipLimitsV1.MaximumRows, result.SourceRowCount);
        Assert.Equal(ProductPaintMembershipLimitsV1.MaximumRows, result.Rows.Count);
    }

    [Fact]
    public void More_than_five_thousand_data_rows_are_refused_without_returning_an_applicable_prefix()
    {
        var codes = Enumerable.Range(1, ProductPaintMembershipLimitsV1.MaximumRows + 1)
            .Select(number => "SNT-" + number.ToString(CultureInfo.InvariantCulture)).ToArray();

        Refused(Workbook(StandardSheet(codes)));
    }

    [Theory]
    [InlineData("mapped")]
    [InlineData("ignored")]
    [InlineData("cached_text")]
    public void A_formula_never_uses_its_cached_value_as_a_product_code(string location)
    {
        var formula = "<c r=\"A{row}\" t=\"str\"><f>CONCAT(\"SNT\",\"-001\")</f><v>SNT-001</v></c>";
        var row = location == "ignored" ? Row(2, Text("A", "SNT-001"), formula.Replace("A{row}", "B{row}", StringComparison.Ordinal))
            : Row(2, location == "cached_text" ? formula : formula.Replace(" t=\"str\"", "", StringComparison.Ordinal));

        Refused(Workbook(Sheet(Row(1, Text("A", DefaultColumn)), row)));
    }

    [Theory]
    [InlineData("vba")]
    [InlineData("macro_sheet")]
    [InlineData("macro_type")]
    [InlineData("activex")]
    public void A_macro_or_active_content_package_is_refused(string kind)
    {
        KeyValuePair<string, string> extra = kind switch
        {
            "vba" => new KeyValuePair<string, string>("xl/vbaProject.bin", "SENTETIK"),
            "macro_sheet" => new("xl/macrosheets/sheet1.xml", StandardSheet("SNT-002")),
            "activex" => new("xl/activeX/activeX1.bin", "SENTETIK"),
            _ => new("SENTETIK.txt", "SENTETIK"),
        };
        var contentTypes = kind == "macro_type" ? Types().Replace("spreadsheetml.sheet.main+xml",
            "ms-excel.sheet.macroEnabled.main+xml", StringComparison.Ordinal) : null;

        Refused(Workbook(StandardSheet("SNT-001"), contentTypes: contentTypes, extras: [extra]));
    }

    [Theory]
    [InlineData("external_mode")]
    [InlineData("http")]
    [InlineData("file")]
    [InlineData("external_part")]
    public void External_relationships_and_external_link_parts_are_never_followed(string fault)
    {
        var mode = fault == "external_mode" ? " TargetMode=\"External\"" : "";
        var target = fault switch { "http" => "https://example.invalid/sheet.xml", "file" => "file:///SENTETIK", _ => "worksheets/import.xml" };
        KeyValuePair<string, string>[] extra = fault == "external_part"
            ? new[] { new KeyValuePair<string, string>("xl/externalLinks/externalLink1.xml", "<SENTETIK/>") } : [];

        Refused(Workbook(StandardSheet("SNT-001"), target: target, targetMode: mode, extras: extra));
    }

    [Theory]
    [InlineData("../../outside.xml")]
    [InlineData("worksheets\\import.xml")]
    [InlineData("worksheets/import.xml?x=1")]
    [InlineData("worksheets/import.xml#x")]
    [InlineData("worksheets/%2e%2e/import.xml")]
    public void An_unsafe_worksheet_target_is_refused(string target)
    {
        Refused(Workbook(StandardSheet("SNT-001"), target: target));
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("A3")]
    [InlineData("A2other")]
    [InlineData("$A$2")]
    [InlineData("A02")]
    [InlineData("XFE2")]
    [InlineData("2")]
    [InlineData("a2")]
    public void A_cell_address_must_name_its_real_row_and_a_bounded_worksheet_column(string reference)
    {
        var sheet = Sheet(Row(1, Text("A", DefaultColumn)),
            $"<row r=\"2\"><c r=\"{Escape(reference)}\" t=\"inlineStr\"><is><t>SNT-001</t></is></c></row>");

        Refused(Workbook(sheet));
    }

    [Theory]
    [InlineData("cell")]
    [InlineData("row")]
    [InlineData("relationship")]
    public void Duplicate_cells_rows_or_relationship_ids_do_not_select_an_arbitrary_value(string duplicate)
    {
        var data = duplicate == "cell" ? Row(2, Text("A", "SNT-001"), Text("A", "SNT-002"))
            : Row(2, Text("A", "SNT-001"));
        if (duplicate == "row") data += Row(2, Text("A", "SNT-002"));
        var relation = duplicate == "relationship"
            ? $"<Relationship Id=\"rIdImport\" Type=\"{DocumentRelationshipNamespace}/worksheet\" Target=\"worksheets/import.xml\"/>" : "";

        Refused(Workbook(Sheet(Row(1, Text("A", DefaultColumn)), data), extraRelationship: relation));
    }

    [Theory]
    [InlineData("sheet")]
    [InlineData("workbook")]
    [InlineData("shared_strings")]
    [InlineData("content_types")]
    public void DTDs_are_prohibited_in_every_parsed_XML_part(string part)
    {
        const string dtd = "<!DOCTYPE synthetic [<!ENTITY payload 'SENTETIK'>]>";
        var sheet = StandardSheet("SNT-001");
        var workbook = (string?)null;
        var shared = (string?)null;
        var types = (string?)null;
        switch (part)
        {
            case "sheet": sheet = dtd + sheet; break;
            case "workbook": workbook = dtd + WorkbookXml(false); break;
            case "shared_strings": shared = dtd + $"<sst xmlns=\"{MainNamespace}\"><si><t>&payload;</t></si></sst>"; break;
            case "content_types": types = dtd + Types(); break;
        }

        Refused(Workbook(sheet, sharedStrings: shared, workbookXml: workbook, contentTypes: types));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("SENTETIK")]
    public void An_invalid_shared_string_reference_never_becomes_empty_or_numeric_code_text(string index)
    {
        var sheet = Sheet(Row(1, Text("A", DefaultColumn)), Row(2, $"<c r=\"A{{row}}\" t=\"s\"><v>{index}</v></c>"));
        var shared = $"<sst xmlns=\"{MainNamespace}\"><si><t>SNT-001</t></si></sst>";

        Refused(Workbook(sheet, sharedStrings: shared));
    }

    [Fact]
    public void Shared_string_count_is_bounded_even_when_an_oversized_table_is_compressible()
    {
        var shared = $"<sst xmlns=\"{MainNamespace}\">"
            + string.Concat(Enumerable.Repeat("<si><t>x</t></si>", ProductPaintMembershipWorkbook.MaximumSharedStrings + 1)) + "</sst>";

        Refused(Workbook(StandardSheet("SNT-001"), sharedStrings: shared));
    }

    [Theory]
    [InlineData("duplicate_part")]
    [InlineData("case_ambiguous_part")]
    [InlineData("unsafe_part")]
    [InlineData("too_many_parts")]
    [InlineData("expanded_part")]
    [InlineData("expanded_total")]
    public void ZIP_part_names_count_and_actual_expanded_content_are_bounded(string fault)
    {
        IEnumerable<KeyValuePair<string, string>> extras = fault switch
        {
            "duplicate_part" => [new("xl/workbook.xml", WorkbookXml(false))],
            "case_ambiguous_part" => [new("XL/WORKBOOK.XML", WorkbookXml(false))],
            "unsafe_part" => [new("../SENTETIK.txt", "SENTETIK")],
            "too_many_parts" => Enumerable.Range(1, ProductPaintMembershipWorkbook.MaximumZipEntries)
                .Select(index => new KeyValuePair<string, string>($"SENTETIK-{index}.txt", "x")),
            "expanded_part" => [new("SENTETIK.txt", new string('x', ProductPaintMembershipWorkbook.MaximumPartBytes + 1))],
            "expanded_total" => Enumerable.Range(1, 3)
                .Select(index => new KeyValuePair<string, string>($"SENTETIK-{index}.txt", new string('x', 6_000_000))),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Refused(Workbook(StandardSheet("SNT-001"), extras: extras));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("text")]
    [InlineData("oversized")]
    [InlineData("malformed_xml")]
    [InlineData("empty_sheet")]
    [InlineData("headers_only")]
    public void Invalid_or_oversized_files_never_return_ready_rows(string fault)
    {
        byte[] bytes = fault switch
        {
            "empty" => [],
            "text" => Encoding.UTF8.GetBytes("SENTETIK;SNT-001"),
            "oversized" => new byte[ProductPaintMembershipLimitsV1.MaximumFileBytes + 1],
            "malformed_xml" => Workbook("<worksheet>"),
            "empty_sheet" => Workbook(Sheet()),
            "headers_only" => Workbook(Sheet(Row(1, Text("A", DefaultColumn)))),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Refused(bytes);
    }

    private static void Refused(byte[] content)
    {
        var result = ProductPaintMembershipWorkbook.Read(content, DefaultColumn);
        Assert.False(result.Ready);
        Assert.Empty(result.Rows);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error => Assert.Equal(ProductPaintMembershipCodesV1.InvalidFile, error.Code));
    }

    private static string StandardSheet(params string[] codes) => Sheet(new[] { Row(1, Text("A", DefaultColumn)) }
        .Concat(codes.Select((code, index) => Row(index + 2, Text("A", code)))).ToArray());
    private static string Sheet(params string[] rows) => $"<worksheet xmlns=\"{MainNamespace}\"><sheetData>"
        + string.Concat(rows) + "</sheetData></worksheet>";
    private static string Row(int number, params string[] cells) => $"<row r=\"{number}\">"
        + string.Concat(cells.Select(cell => cell.Replace("{row}", number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))) + "</row>";
    private static string Text(string column, string text) => $"<c r=\"{column}{{row}}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">"
        + Escape(text) + "</t></is></c>";
    private static string Escape(string text) => SecurityElement.Escape(text)!;

    private static byte[] Workbook(string sheet, string? secondSheet = null, string? sharedStrings = null,
        string target = "worksheets/import.xml", string targetMode = "", string extraRelationship = "",
        string? workbookXml = null, string? contentTypes = null, IEnumerable<KeyValuePair<string, string>>? extras = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", contentTypes ?? Types());
            Add(archive, "_rels/.rels", $"<Relationships xmlns=\"{RelationshipNamespace}\"><Relationship Id=\"rIdOffice\" "
                + $"Type=\"{DocumentRelationshipNamespace}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(archive, "xl/workbook.xml", workbookXml ?? WorkbookXml(secondSheet is not null));
            Add(archive, "xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{RelationshipNamespace}\">"
                + (secondSheet is null ? "" : $"<Relationship Id=\"rIdDecoy\" Type=\"{DocumentRelationshipNamespace}/worksheet\" Target=\"worksheets/sheet1.xml\"/>")
                + $"<Relationship Id=\"rIdImport\" Type=\"{DocumentRelationshipNamespace}/worksheet\" Target=\"{Escape(target)}\"{targetMode}/>"
                + extraRelationship + "</Relationships>");
            Add(archive, "xl/worksheets/import.xml", sheet);
            if (secondSheet is not null) Add(archive, "xl/worksheets/sheet1.xml", secondSheet);
            if (sharedStrings is not null) Add(archive, "xl/sharedStrings.xml", sharedStrings);
            if (extras is not null)
                foreach (var (path, value) in extras) Add(archive, path, value);
        }
        return output.ToArray();
    }

    private static string WorkbookXml(bool secondSheet) => $"<workbook xmlns=\"{MainNamespace}\" xmlns:r=\"{DocumentRelationshipNamespace}\"><sheets>"
        + "<sheet name=\"Boya ürünleri\" sheetId=\"2\" r:id=\"rIdImport\"/>"
        + (secondSheet ? "<sheet name=\"SENTETIK-decoy\" sheetId=\"1\" r:id=\"rIdDecoy\"/>" : "") + "</sheets></workbook>";
    private static string Types() => "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
        + "<Override PartName=\"/xl/worksheets/import.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>";
    private static void Add(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
