using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.DocumentGeneration.Pdf;
using UglyToad.PdfPig;

namespace GoymenERP.DocumentGeneration.Pdf.Tests;

public sealed class TechnicalPdfGeneratorTests
{
    private const string GoldenTurkishText =
        "İ ı Ş ş Ğ ğ Ü ü Ö ö Ç ç — Satış sipariş sözleşmesi onaylıdır.";

    [Fact]
    public void Visible_file_name_builder_preserves_nfc_turkish_and_exact_master_pattern()
    {
        var normalized = PdfVisibleFileName.Build(new PdfDocumentFileNameParts(
            "Satıs\u0327 Sipariş Sözleşmesi",
            "SPS-2026-01842",
            "ABC/Demir\u200F",
            new DateOnly(2026, 8, 25),
            1,
            "Onaylı<>:\u0001. "));

        Assert.Equal(
            "Satış_Sipariş_Sözleşmesi_SPS-2026-01842_ABC_Demir_2026-08-25_v01_Onaylı_.pdf",
            normalized);
        Assert.True(normalized.IsNormalized(NormalizationForm.FormC));
        Assert.DoesNotContain(normalized, character =>
            "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal)
            || CharUnicodeInfo.GetUnicodeCategory(character) is
                UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate);
        Assert.False(normalized.EndsWith(' ') || normalized.EndsWith('.'));
        Assert.Equal(normalized, Path.GetFileName(normalized));
        Assert.EndsWith(".pdf", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_file_name_is_independent_from_opaque_technical_id()
    {
        var generator = new TechnicalPdfGenerator();
        var firstTechnicalId = Guid.Parse("a9d42d1e-d92d-4e12-a83a-d34ba34cb753");
        var secondTechnicalId = Guid.Parse("f71c056f-ce71-48c6-a7ed-441d4886dd21");
        var first = generator.Generate(new PdfGenerationRequest(
            firstTechnicalId,
            CreateFileNameParts(),
            [GoldenTurkishText]));
        var second = generator.Generate(new PdfGenerationRequest(
            secondTechnicalId,
            CreateFileNameParts(),
            [GoldenTurkishText]));

        Assert.Equal(
            "Satış_Sipariş_Sözleşmesi_SPS-2026-01842_ABC_Demir_2026-08-25_v01_Onaylı.pdf",
            first.VisibleFileName);
        Assert.Equal(first.VisibleFileName, second.VisibleFileName);
        Assert.Equal(firstTechnicalId, first.TechnicalId);
        Assert.Equal(secondTechnicalId, second.TechnicalId);
        Assert.DoesNotContain(first.TechnicalId.ToString(), first.VisibleFileName, StringComparison.Ordinal);
        Assert.DoesNotContain(second.TechnicalId.ToString(), second.VisibleFileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("prn.report")]
    [InlineData("LPT9.pdf")]
    public void Visible_file_name_rejects_reserved_device_stems(string requestedName)
    {
        Assert.Throws<ArgumentException>(() => PdfVisibleFileName.Normalize(requestedName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>:\\/?*\u0001")]
    public void Visible_file_name_rejects_empty_or_only_unsafe_input(string requestedName)
    {
        Assert.Throws<ArgumentException>(() => PdfVisibleFileName.Normalize(requestedName));
    }

    [Fact]
    public void Visible_file_name_sanitization_is_idempotent_and_path_safe()
    {
        var first = PdfVisibleFileName.Normalize("../..\\İrsaliye<>:\"/\\|?*\u0001\u0085\u200F. ");
        var second = PdfVisibleFileName.Normalize(first);
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "erp-000h-safe-root"));
        var resolved = Path.GetFullPath(Path.Combine(root, first));

        Assert.Equal(".._.._İrsaliye_.pdf", first);
        Assert.Equal(first, second);
        Assert.StartsWith(root + Path.DirectorySeparatorChar, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(first, character =>
            CharUnicodeInfo.GetUnicodeCategory(character) is
                UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate);
    }

    [Fact]
    public void Structured_file_name_is_culture_invariant_and_nfc_canonical()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkishCultureResult = PdfVisibleFileName.Build(CreateFileNameParts());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var arabicCultureResult = PdfVisibleFileName.Build(CreateFileNameParts());

            Assert.Equal(turkishCultureResult, arabicCultureResult);
            Assert.Equal(turkishCultureResult, turkishCultureResult.Normalize(NormalizationForm.FormC));
            Assert.Equal(turkishCultureResult, PdfVisibleFileName.Normalize(turkishCultureResult));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Visible_file_name_rejects_length_beyond_portable_utf16_or_utf8_limit()
    {
        var maximumAsciiName = string.Concat(new string('A', 251), ".pdf");
        Assert.Equal(maximumAsciiName, PdfVisibleFileName.Normalize(maximumAsciiName));

        Assert.Throws<ArgumentException>(() =>
            PdfVisibleFileName.Normalize(string.Concat(new string('A', 252), ".pdf")));
        Assert.Throws<ArgumentException>(() =>
            PdfVisibleFileName.Normalize(string.Concat(new string('İ', 126), ".pdf")));
    }

    [Fact]
    public void Structured_file_name_collapses_component_boundary_separator_runs()
    {
        var result = PdfVisibleFileName.Build(new PdfDocumentFileNameParts(
            "A/",
            "/B",
            "_C",
            new DateOnly(2026, 8, 25),
            1,
            "Taslak"));

        Assert.Equal("A_B_C_2026-08-25_v01_Taslak.pdf", result);
        Assert.DoesNotContain("__", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_pdf_is_one_page_semantic_golden_with_embedded_noto_sans()
    {
        string[] semanticLines = ["Teknik PDF PoC", GoldenTurkishText];
        var artifact = new TechnicalPdfGenerator().Generate(new PdfGenerationRequest(
            Guid.Parse("eb4b6211-2afc-46e1-9a55-1a96c3c06897"),
            CreateFileNameParts(),
            semanticLines));

        Assert.Equal("application/pdf", artifact.MediaType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(artifact.Content, 0, 5));

        using var pdf = PdfDocument.Open(artifact.Content);
        var page = Assert.Single(pdf.GetPages());
        Assert.InRange(page.Width, 594.9, 595.1);
        Assert.InRange(page.Height, 841.9, 842.1);
        Assert.Equal(string.Concat(semanticLines), page.Text);
        Assert.Equal(
            "10C42BE3B5DCFC524E4E7ECD14698A7526CBD96B8361D5836258BB547AFB876E",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text))));
        Assert.Contains(page.Letters, letter =>
            letter.FontName?.Contains("Noto Sans Regular", StringComparison.OrdinalIgnoreCase) == true);

        var pdfSyntax = Encoding.Latin1.GetString(artifact.Content);
        Assert.Contains("/FontFile2", pdfSyntax, StringComparison.Ordinal);
        Assert.Contains("/ToUnicode", pdfSyntax, StringComparison.Ordinal);
        Assert.Contains("Noto#20Sans#20Regular", pdfSyntax, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Subtype /Image", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/JavaScript", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/OpenAction", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/AA", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/Launch", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/EmbeddedFiles", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("Göymen", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logo", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("antet", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, pdfSyntax, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, pdfSyntax, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), pdfSyntax, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vendored_noto_sans_regular_hash_is_locked()
    {
        var fontPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "NotoSans-Regular.ttf");

        Assert.True(File.Exists(fontPath));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath)));
        Assert.Equal(
            "B85C38ECEA8A7CFB39C24E395A4007474FA5A4FC864F6EE33309EB4948D232D5",
            hash);

        var licensePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "OFL-1.1.txt");
        Assert.True(File.Exists(licensePath));
        Assert.Equal(
            "0DAB92D0544F7B233403F14B84A663BDBFA746982EDA629E7F4F9FFE1B036FEB",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(licensePath))));

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        Assert.True(File.Exists(noticesPath));
        Assert.Contains("PDFsharp 6.2.4", File.ReadAllText(noticesPath), StringComparison.Ordinal);

        var pdfsharpLicensePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Licenses",
            "PDFsharp-MIT.txt");
        Assert.True(File.Exists(pdfsharpLicensePath));
        Assert.Equal(1147, new FileInfo(pdfsharpLicensePath).Length);
        Assert.Equal(
            "3C648A5072BD7F7386CA28687780489A708A57ED75F13927BC16DD3E34AF3BF7",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdfsharpLicensePath))));

        var pdfsharpProvenancePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Licenses",
            "PDFsharp-6.2.4-PROVENANCE.md");
        Assert.True(File.Exists(pdfsharpProvenancePath));
        Assert.Contains(
            "9c3863a71b12067ff36e68a92c1a1f260114e2d1",
            File.ReadAllText(pdfsharpProvenancePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_generation_keeps_request_identity_and_content_isolated()
    {
        var requests = Enumerable.Range(1, 16)
            .Select(index => new PdfGenerationRequest(
                Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
                new PdfDocumentFileNameParts(
                    "Teknik Sözleşme",
                    $"SPS-{index:D5}",
                    $"Cari {index:D2}",
                    new DateOnly(2026, 8, 25),
                    index,
                    "Taslak"),
                [$"İstek {index:D2} — İıŞşĞğÜüÖöÇç"]))
            .ToArray();

        var artifacts = requests
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(request => new TechnicalPdfGenerator().Generate(request))
            .ToArray();

        Assert.Equal(requests.Length, artifacts.Length);
        foreach (var artifact in artifacts)
        {
            var request = Assert.Single(requests, candidate => candidate.TechnicalId == artifact.TechnicalId);
            using var pdf = PdfDocument.Open(artifact.Content);
            Assert.Equal(Assert.Single(request.Lines), Assert.Single(pdf.GetPages()).Text);
            Assert.Equal(PdfVisibleFileName.Build(request.FileNameParts), artifact.VisibleFileName);
        }
    }

    [Fact]
    public void Generation_rejects_content_that_cannot_fit_the_single_page()
    {
        var request = new PdfGenerationRequest(
            Guid.NewGuid(),
            CreateFileNameParts(),
            Enumerable.Repeat("Teknik satır", 100).ToArray());

        Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));
    }

    [Fact]
    public void Generation_rejects_a_line_wider_than_the_page_instead_of_truncating()
    {
        var request = new PdfGenerationRequest(
            Guid.NewGuid(),
            CreateFileNameParts(),
            [new string('İ', 1_000)]);

        Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));
    }

    private static PdfDocumentFileNameParts CreateFileNameParts() => new(
        "Satış Sipariş Sözleşmesi",
        "SPS-2026-01842",
        "ABC Demir",
        new DateOnly(2026, 8, 25),
        1,
        "Onaylı");
}
