using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// B-26 / FNS-D52: the bank-statement import kinds (XLSX, SWIFT MT940, CSV) as pure decisions. A kind is accepted only
/// when the owner record opts in, its bytes pass the structural check and the declared type agrees or is unknown; an
/// active workbook (macros, embedded objects, external links), an encrypted one, a binary or markup renamed as text and
/// a malformed statement are refused with the existing stable codes. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentStatementContentRulesTests
{
    private const long Bound = 64 * 1024;
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly IReadOnlyList<DocumentFileKind> StatementOwner =
        [.. DocumentContentPolicy.StandardKinds, .. DocumentContentPolicy.BankStatementImportKinds];

    internal static readonly byte[] Csv = Encoding.UTF8.GetBytes(
        "﻿Tarih;Açıklama;Tutar;Bakiye\r\n01.10.2026;\"SENTETIK; havale\";1.250,00;9.750,00\r\n");

    internal static readonly byte[] Mt940 = Encoding.ASCII.GetBytes(
        ":20:SENTETIK-STMT-1\r\n:25:SENTETIK/0001\r\n:28C:00001/001\r\n:60F:C261001TRY1000,00\r\n" +
        ":61:2610011001C250,00NTRFSENTETIK\r\n:86:SENTETIK HAVALE\r\n:62F:C261001TRY1250,00\r\n");

    internal static byte[] Xlsx => Workbook();

    [Theory]
    [InlineData("ekstre.xlsx", XlsxType, DocumentFileKind.Xlsx)]
    [InlineData("EKSTRE.XLSX", "", DocumentFileKind.Xlsx)]
    [InlineData("ekstre.sta", "text/plain", DocumentFileKind.Mt940)]
    [InlineData("ekstre.mt940", "application/octet-stream", DocumentFileKind.Mt940)]
    [InlineData("ekstre.940", "", DocumentFileKind.Mt940)]
    [InlineData("ekstre.txt", "text/plain; charset=utf-8", DocumentFileKind.Mt940)]
    [InlineData("ekstre.csv", "text/csv", DocumentFileKind.Csv)]
    [InlineData("ekstre.csv", "application/vnd.ms-excel", DocumentFileKind.Csv)]
    [InlineData("ekstre.csv", "text/plain", DocumentFileKind.Csv)]
    public void A_statement_kind_is_accepted_with_its_canonical_type_when_the_owner_opts_in_and_the_bytes_prove_it(string name,
        string declared, DocumentFileKind kind)
    {
        var decision = DocumentContentPolicy.Classify(Name(name), declared, Content(kind), Bound, StatementOwner);

        Assert.True(decision.IsAccepted, decision.RefusalCode);
        Assert.Equal(kind, decision.Kind!.Kind);
        Assert.Equal(DocumentContentPolicy.Info(kind).ContentType, decision.Kind.ContentType);
    }

    [Theory]
    [InlineData("ekstre.xlsx", DocumentFileKind.Xlsx)]
    [InlineData("ekstre.sta", DocumentFileKind.Mt940)]
    [InlineData("ekstre.csv", DocumentFileKind.Csv)]
    public void A_statement_kind_is_refused_as_not_allowed_by_an_owner_that_keeps_the_standard_list(string name, DocumentFileKind kind)
    {
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed,
            DocumentContentPolicy.Classify(Name(name), null, Content(kind), Bound).RefusalCode);
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed,
            DocumentContentPolicy.Classify(Name(name), null, Content(kind), Bound, DocumentContentPolicy.StandardKinds).RefusalCode);
        // An owner that opts in to the statement kinds only still refuses a PDF.
        Assert.Equal(DocumentRuleCodes.TypeNotAllowed, DocumentContentPolicy.Classify(Name("a.pdf"), "application/pdf",
            DocumentContentRulesTests.Pdf, Bound, DocumentContentPolicy.BankStatementImportKinds).RefusalCode);
    }

    [Fact]
    public void The_known_kinds_keep_the_standard_list_first_and_the_statement_kinds_are_listed_per_owner()
    {
        Assert.Equal([DocumentFileKind.Pdf, DocumentFileKind.Png, DocumentFileKind.Jpeg], DocumentContentPolicy.StandardKinds);
        Assert.Equal([DocumentFileKind.Xlsx, DocumentFileKind.Mt940, DocumentFileKind.Csv], DocumentContentPolicy.BankStatementImportKinds);
        Assert.Equal(Enum.GetValues<DocumentFileKind>(), DocumentContentPolicy.KnownKinds.Select(info => info.Kind));
        Assert.Equal(["application/pdf", "image/png", "image/jpeg"],
            DocumentContentPolicy.InfosOf(DocumentContentPolicy.StandardKinds).Select(info => info.ContentType));
        Assert.Equal([XlsxType, "text/plain", "text/csv"],
            DocumentContentPolicy.InfosOf([DocumentFileKind.Csv, DocumentFileKind.Mt940, DocumentFileKind.Xlsx]).Select(info => info.ContentType));
        // Every extension names one kind only, so the extension never picks between two kinds.
        var extensions = DocumentContentPolicy.KnownKinds.SelectMany(info => info.Extensions).ToList();
        Assert.Equal(extensions.Count, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("xl/vbaProject.bin")]
    [InlineData("xl/macrosheets/sheet1.xml")]
    [InlineData("xl/activeX/activeX1.xml")]
    [InlineData("xl/embeddings/oleObject1.bin")]
    [InlineData("xl/externalLinks/externalLink1.xml")]
    [InlineData("customUI/VBAPROJECTSIGNATURE.bin")]
    public void A_workbook_naming_an_active_part_is_refused_as_a_mismatch(string activePart)
    {
        var decision = DocumentContentPolicy.Classify(Name("ekstre.xlsx"), XlsxType, Workbook(activePart), Bound, StatementOwner);

        Assert.Equal(DocumentRuleCodes.ContentMismatch, decision.RefusalCode);
    }

    [Fact]
    public void A_workbook_that_is_encrypted_lacks_its_package_parts_or_has_an_unsafe_part_name_is_refused_as_a_mismatch()
    {
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Encrypted(Workbook())).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Zip(("[Content_Types].xml", "<Types/>"))).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Zip(("xl/workbook.xml", "<workbook/>"))).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Workbook("../../evil.xml")).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Workbook("/xl/root.xml")).RefusalCode);
        // A plain ZIP, a .docx package and a truncated workbook are not passive workbooks either.
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", Zip(("readme.txt", "SENTETIK"))).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.xlsx", Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<document/>"))).RefusalCode);
        var workbook = Workbook();
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", workbook[..^1]).RefusalCode);
        // A PDF renamed .xlsx, and a workbook declared as something else.
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.xlsx", DocumentContentRulesTests.Pdf).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            DocumentContentPolicy.Classify(Name("ekstre.xlsx"), "application/vnd.ms-excel.sheet.macroEnabled.12", workbook, Bound, StatementOwner).RefusalCode);
    }

    [Fact]
    public void A_binary_markup_or_unbalanced_file_named_csv_is_refused_as_a_mismatch()
    {
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", DocumentContentRulesTests.Pdf).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", Xlsx).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.csv", "  <html><body><script>alert(1)</script>,x</body></html>"u8.ToArray()).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "Tarih;Tutar\r\n\"01.10.2026;1,00\r\n"u8.ToArray()).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "tek-sutun\r\n1\r\n"u8.ToArray()).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "a,b\u0000\r\n"u8.ToArray()).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "a,b\u001B[2J\r\n"u8.ToArray()).RefusalCode);
        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("a;b\r\n")];
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", utf16).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "   \r\n"u8.ToArray()).RefusalCode);
        // Tab and comma tables, and a header whose only delimiter is outside the quotes, are tables.
        Assert.True(Statement("ekstre.csv", "Tarih\tTutar\r\n"u8.ToArray()).IsAccepted);
        Assert.True(Statement("ekstre.csv", "\"Tarih, saat\",Tutar\n"u8.ToArray()).IsAccepted);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.csv", "\"Tarih, saat\"\n"u8.ToArray()).RefusalCode);
        // A declared type that contradicts the kind is refused even over good bytes.
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            DocumentContentPolicy.Classify(Name("ekstre.csv"), "text/html", Csv, Bound, StatementOwner).RefusalCode);
    }

    [Fact]
    public void An_mt940_statement_needs_its_tags_at_line_starts_in_order_and_may_carry_its_swift_envelope()
    {
        var enveloped = Encoding.ASCII.GetBytes(
            "{1:F01SENTETIKXXXX0000000000}{2:I940SENTETIKXXXXN}{4:\r\n:20:SENTETIK-STMT-2\r\n:25:SENTETIK/0002\r\n" +
            ":28C:00002/001\r\n:60M:C261002TRY10,00\r\n:62M:C261002TRY10,00\r\n-}");
        Assert.True(Statement("ekstre.sta", enveloped).IsAccepted);
        var inlineTextBlock = Encoding.ASCII.GetBytes(
            "{1:F01SENTETIKXXXX0000000000}{4::20:SENTETIK\r\n:25:X\r\n:28C:1\r\n:60F:C261003TRY1,00\r\n:62F:C261003TRY1,00\r\n-}");
        Assert.True(Statement("ekstre.sta", inlineTextBlock).IsAccepted);

        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.sta", Encoding.ASCII.GetBytes(":20:X\r\n:25:X\r\n:28C:1\r\n:60F:C261001TRY1,00\r\n")).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.sta", Encoding.ASCII.GetBytes(":20:X\r\n:28C:1\r\n:25:X\r\n:60F:C261001TRY1,00\r\n:62F:C261001TRY1,00\r\n")).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.sta", Encoding.ASCII.GetBytes(":20:X :25:X :28C:1 :60F:C261001TRY1,00 :62F:C261001TRY1,00\r\n")).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            Statement("ekstre.txt", Encoding.ASCII.GetBytes("Merhaba\r\n:20:X\r\n:25:X\r\n:28C:1\r\n:60F:C1\r\n:62F:C1\r\n")).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.sta", Csv).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.sta", DocumentContentRulesTests.Pdf).RefusalCode);
        var withNul = (byte[])Mt940.Clone();
        withNul[^3] = 0x00;
        Assert.Equal(DocumentRuleCodes.ContentMismatch, Statement("ekstre.sta", withNul).RefusalCode);
        Assert.Equal(DocumentRuleCodes.ContentMismatch,
            DocumentContentPolicy.Classify(Name("ekstre.sta"), "text/csv", Mt940, Bound, StatementOwner).RefusalCode);
    }

    [Fact]
    public void An_empty_statement_and_one_over_the_existing_bound_are_refused_before_the_content_is_inspected()
    {
        Assert.Equal(DocumentRuleCodes.Empty, Statement("ekstre.csv", []).RefusalCode);
        var large = new byte[Bound + 1];
        Csv.CopyTo(large, 0);
        large.AsSpan(Csv.Length).Fill((byte)'x');
        Assert.Equal(DocumentRuleCodes.TooLarge, Statement("ekstre.csv", large).RefusalCode);
        Assert.True(Statement("ekstre.csv", large.AsSpan(0, (int)Bound).ToArray()).IsAccepted);
    }

    private static DocumentContentDecision Statement(string name, byte[] content) =>
        DocumentContentPolicy.Classify(Name(name), null, content, Bound, StatementOwner);

    private static DocumentFileName Name(string value) => DocumentFileName.TryCreate(value)!;

    internal static byte[] Content(DocumentFileKind kind) => kind switch
    {
        DocumentFileKind.Xlsx => Xlsx,
        DocumentFileKind.Mt940 => Mt940,
        DocumentFileKind.Csv => Csv,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>A minimal SpreadsheetML package (SENTETIK), with any extra part names.</summary>
    internal static byte[] Workbook(params string[] extraParts) => Zip(
        [
            ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>"),
            ("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>"),
            ("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row><c t=\"inlineStr\"><is><t>SENTETIK</t></is></c></row></sheetData></worksheet>"),
            .. extraParts.Select(part => (part, "SENTETIK")),
        ]);

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>The same package with the encrypted flag (general purpose bit 0) set on its first central directory record.</summary>
    private static byte[] Encrypted(byte[] zip)
    {
        var copy = (byte[])zip.Clone();
        var end = copy.Length - 22;
        Assert.Equal(0x06054B50u, BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(end)));
        var directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(end + 16));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(directory + 8));
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(directory + 8), (ushort)(flags | 1));
        return copy;
    }
}
