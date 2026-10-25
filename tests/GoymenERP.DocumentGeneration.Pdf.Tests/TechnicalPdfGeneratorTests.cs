using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.DocumentGeneration.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

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
    [InlineData("COM\u00B9")]
    [InlineData("com\u00B9.pdf")]
    [InlineData("COM\u00B2")]
    [InlineData("com\u00B2.pdf")]
    [InlineData("COM\u00B3")]
    [InlineData("com\u00B3.pdf")]
    [InlineData("LPT\u00B9")]
    [InlineData("lpt\u00B9.pdf")]
    [InlineData("LPT\u00B2")]
    [InlineData("lpt\u00B2.pdf")]
    [InlineData("LPT\u00B3")]
    [InlineData("lpt\u00B3.pdf")]
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
    public void Visible_file_name_rejects_oversized_raw_input_before_unsafe_runs_can_collapse()
    {
        var requestedName = string.Concat("A", new string('*', 255));

        var exception = Assert.Throws<ArgumentException>(() => PdfVisibleFileName.Normalize(requestedName));

        Assert.Equal("requestedVisibleFileName", exception.ParamName);
        Assert.Contains("raw PDF file-name input", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Structured_file_name_rejects_separator_only_mandatory_component()
    {
        var parts = CreateFileNameParts() with { DocumentType = "___" };

        var exception = Assert.Throws<ArgumentException>(() => PdfVisibleFileName.Build(parts));

        Assert.Equal("DocumentType", exception.ParamName);
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
        Assert.Contains("/Subtype/Type0", pdfSyntax, StringComparison.Ordinal);
        Assert.Contains("/Encoding/Identity-H", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/Encoding/WinAnsiEncoding", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype/TrueType", pdfSyntax, StringComparison.Ordinal);
        var vendoredFontPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "NotoSans-Regular.ttf");
        var vendoredFontBytes = File.ReadAllBytes(vendoredFontPath);
        Assert.Matches($@"/Length1\s+{vendoredFontBytes.Length}\b", pdfSyntax);
        Assert.Contains("Noto#20Sans#20Regular", pdfSyntax, StringComparison.OrdinalIgnoreCase);

        using var structuralStream = new MemoryStream(artifact.Content, writable: false);
        using var structuralPdf = PdfSharp.Pdf.IO.PdfReader.Open(
            structuralStream,
            PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var structuralPage = structuralPdf.Pages[0];
        var fonts = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(
            structuralPage.Resources.Elements.GetDictionary("/Font"));
        var type0Font = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(
            fonts.Elements.GetDictionary(Assert.Single(fonts.Elements.Keys)));
        Assert.Equal("/Type0", type0Font.Elements.GetName("/Subtype"));
        Assert.Equal("/Identity-H", type0Font.Elements.GetName("/Encoding"));
        Assert.NotNull(type0Font.Elements.GetDictionary("/ToUnicode"));
        var descendantFonts = Assert.IsType<PdfSharp.Pdf.PdfArray>(
            type0Font.Elements.GetArray("/DescendantFonts"));
        Assert.Single(descendantFonts.Elements);
        var cidFont = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(
            descendantFonts.Elements.GetDictionary(0));
        Assert.Equal("/CIDFontType2", cidFont.Elements.GetName("/Subtype"));
        var fontDescriptor = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(
            cidFont.Elements.GetDictionary("/FontDescriptor"));
        var embeddedFont = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(
            fontDescriptor.Elements.GetDictionary("/FontFile2"));
        Assert.Equal(vendoredFontBytes, embeddedFont.Stream.UnfilteredValue);
        Assert.DoesNotContain("/Subtype /Image", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/JavaScript", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/OpenAction", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/AA", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/Launch", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("/EmbeddedFiles", pdfSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("Göymen", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logo", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("antet", page.Text, StringComparison.OrdinalIgnoreCase);
        AssertNoHostIdentifiers(pdf, artifact.Content,
            Environment.UserName, Environment.MachineName, Directory.GetCurrentDirectory());
        Assert.DoesNotContain(Environment.MachineName, pdfSyntax, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), pdfSyntax, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pdf_privacy_guard_does_not_mistake_required_font_names_for_a_short_username()
    {
        var content = CreatePrivacyProbePdf();
        Assert.Contains("/CIDFontType2", Encoding.Latin1.GetString(content), StringComparison.Ordinal);
        using var pdf = PdfDocument.Open(content);

        AssertNoHostIdentifiers(pdf, content, "ci");
    }

    [Theory]
    [InlineData("/Author")]
    [InlineData("/Creator")]
    [InlineData("/LocalHostMarker")]
    public void Pdf_privacy_guard_rejects_host_information_in_standard_and_custom_metadata(string field)
    {
        var content = CreatePrivacyProbePdf(field: field, value: "ci");
        using var pdf = PdfDocument.Open(content);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoHostIdentifiers(pdf, content, "ci"));
    }

    [Fact]
    public void Pdf_privacy_guard_rejects_unicode_encoded_metadata_that_raw_search_misses()
    {
        const string privateMarker = "private-host-canary-d9a57c";
        var content = CreatePrivacyProbePdf(field: "/LocalHostMarker", value: privateMarker, unicode: true);
        Assert.DoesNotContain(privateMarker, Encoding.Latin1.GetString(content), StringComparison.Ordinal);
        using var pdf = PdfDocument.Open(content);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoHostIdentifiers(pdf, content, privateMarker));
    }

    [Fact]
    public void Pdf_privacy_guard_rejects_host_information_in_extracted_page_text()
    {
        var content = CreatePrivacyProbePdf(lines: [GoldenTurkishText, "ci"]);
        using var pdf = PdfDocument.Open(content);
        Assert.Contains("ci", Assert.Single(pdf.GetPages()).Text, StringComparison.Ordinal);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoHostIdentifiers(pdf, content, "ci"));
    }

    [Fact]
    public void Pdf_privacy_guard_never_treats_unresolved_metadata_as_empty()
    {
        var reference = new IndirectReferenceToken(new UglyToad.PdfPig.Core.IndirectReference(1234, 0));

        Assert.Throws<InvalidOperationException>(() => DecodedMetadataValues(reference).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pdf_privacy_guard_rejects_uninspected_catalog_and_page_xmp(bool onPage)
    {
        var content = CreateExtraPayloadPrivacyProbe("private-xmp-canary-2a74", onPage, literal: false);
        using var pdf = PdfDocument.Open(content);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertNoHostIdentifiers(pdf, content, "private-xmp-canary-2a74"));
    }

    [Fact]
    public void Pdf_privacy_guard_rejects_literal_host_information_outside_info_and_page_text()
    {
        var content = CreateExtraPayloadPrivacyProbe("private-catalog-canary-2a74", onPage: false, literal: true);
        using var pdf = PdfDocument.Open(content);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertNoHostIdentifiers(pdf, content, "private-catalog-canary-2a74"));
    }

    [Theory]
    [InlineData("<rdf:Description user='host&amp;machine'/>")]
    [InlineData("<rdf:Description>host&amp;machine</rdf:Description>")]
    [InlineData("<!-- host&amp;machine --><rdf:Description/>")]
    public void Catalog_xmp_exposes_decoded_host_information_without_ignoring_comments(string payload)
    {
        var xml = "<x:xmpmeta xmlns:x='adobe:ns:meta/'>"
            + "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>"
            + payload + "</rdf:RDF></x:xmpmeta>";
        var surface = DecodeCatalogXmp(Encoding.UTF8.GetBytes(xml));

        Assert.Contains(
            payload.StartsWith("<!--", StringComparison.Ordinal) ? "host&amp;machine" : "host&machine",
            surface,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_xmp_rejects_DTD_before_resolving_external_entities()
    {
        const string xml = "<!DOCTYPE x [<!ENTITY host SYSTEM 'file:///not-readable-host-canary'>]>"
            + "<x:xmpmeta xmlns:x='adobe:ns:meta/'>&host;</x:xmpmeta>";

        Assert.Throws<Xunit.Sdk.XunitException>(() => DecodeCatalogXmp(Encoding.UTF8.GetBytes(xml)));
    }

    [Theory]
    [InlineData("page-comment")]
    [InlineData("cmap-comment")]
    [InlineData("page-array-comment")]
    [InlineData("cmap-dictionary-comment")]
    [InlineData("page-unused-string")]
    [InlineData("cmap-unused-string")]
    [InlineData("catalog-name")]
    [InlineData("outer-comment")]
    [InlineData("outer-unreferenced-stream")]
    public void Pdf_privacy_guard_rejects_nonvisible_host_payloads(string channel)
    {
        const string marker = "private-host-canary-9b73";
        var content = CreateNonvisiblePrivacyProbe(channel, marker);
        using var pdf = PdfDocument.Open(content);
        Assert.DoesNotContain(marker, Assert.Single(pdf.GetPages()).Text, StringComparison.Ordinal);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoHostIdentifiers(pdf, content, marker));
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

        var fontProvenancePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "PROVENANCE.md");
        Assert.True(File.Exists(fontProvenancePath));
        Assert.Equal(
            "D17541D4B00CBC1171A49755B2DE010240A8B5E66D51ED4DA88B44C278C46759",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontProvenancePath))));

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

        var exception = Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));

        Assert.Contains("does not fit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_rejects_excessive_line_count_before_element_access_or_enumeration()
    {
        var request = new PdfGenerationRequest(
            Guid.NewGuid(),
            CreateFileNameParts(),
            new CountOnlyReadOnlyList(100));

        var exception = Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));

        Assert.Contains("does not fit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_rejects_a_line_wider_than_the_page_instead_of_truncating()
    {
        var request = new PdfGenerationRequest(
            Guid.NewGuid(),
            CreateFileNameParts(),
            [new string('İ', 1_000)]);

        var exception = Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));

        Assert.Contains("wider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_rejects_enormous_raw_line_before_normalization_or_font_shaping()
    {
        const int firstRejectedRawLineLength = 4_097;
        var request = new PdfGenerationRequest(
            Guid.NewGuid(),
            CreateFileNameParts(),
            [new string('A', firstRejectedRawLineLength)]);

        var exception = Assert.Throws<ArgumentException>(() => new TechnicalPdfGenerator().Generate(request));

        Assert.Contains("raw input limit", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertNoHostIdentifiers(PdfDocument pdf, byte[] content, params string[] identifiers)
    {
        using var input = new MemoryStream(content, writable: false);
        using var structure = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        // Capture original encoded bytes before any library may lazily decode streams.
        var reviewedRawStreams = structure.Internals.GetAllObjects()
            .OfType<PdfSharp.Pdf.PdfDictionary>().Where(dictionary => dictionary.Stream is not null)
            .Select(dictionary => RawStreamIdentity(dictionary.Stream.Value)).ToHashSet();
        var streamSurfaces = InspectSupportedPrivacyPayloads(structure).ToArray();
        // Inspect literal strings throughout the document, not only trailer /Info.
        // Structural names are not strings; font binary bytes remain exactly vendor-locked.
        var surfaces = pdf.GetPages().Select(page => page.Text)
            .Concat(DecodedMetadataValues(pdf.Information.DocumentInformationDictionary))
            .Concat(structure.Internals.GetAllObjects().SelectMany(DecodedObjectStrings))
            .Concat(streamSurfaces)
            .Concat(DecodedSyntaxStrings(content, isDocument: true, reviewedRawStreams: reviewedRawStreams)).ToArray();
        foreach (var identifier in identifiers)
        {
            Assert.NotEmpty(identifier);
            foreach (var surface in surfaces)
            {
                Assert.DoesNotContain(identifier, surface, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IEnumerable<string> InspectSupportedPrivacyPayloads(PdfSharp.Pdf.PdfDocument document)
    {
        var supportedStreams = new HashSet<PdfSharp.Pdf.PdfDictionary>();
        var lockedFontStreams = new HashSet<PdfSharp.Pdf.PdfDictionary>();
        PdfSharp.Pdf.PdfDictionary? catalogMetadata = null;
        if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
        {
            catalogMetadata = Assert.IsAssignableFrom<PdfSharp.Pdf.PdfDictionary>(
                document.Internals.Catalog.Elements.GetDictionary("/Metadata"));
            Assert.Equal("/Metadata", catalogMetadata.Elements.GetName("/Type"));
            Assert.Equal("/XML", catalogMetadata.Elements.GetName("/Subtype"));
            Assert.NotNull(catalogMetadata.Stream);
            supportedStreams.Add(catalogMetadata);
        }
        var expectedFont = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoSans-Regular.ttf"));
        foreach (var page in document.Pages)
        {
            for (var index = 0; index < page.Contents.Elements.Count; index++)
            {
                supportedStreams.Add(Assert.IsAssignableFrom<PdfSharp.Pdf.PdfDictionary>(page.Contents.Elements.GetDictionary(index)));
            }
            var fonts = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(page.Resources.Elements.GetDictionary("/Font"));
            foreach (var key in fonts.Elements.Keys)
            {
                var font = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(fonts.Elements.GetDictionary(key));
                supportedStreams.Add(Assert.IsType<PdfSharp.Pdf.PdfDictionary>(font.Elements.GetDictionary("/ToUnicode")));
                var descendants = Assert.IsType<PdfSharp.Pdf.PdfArray>(font.Elements.GetArray("/DescendantFonts"));
                for (var index = 0; index < descendants.Elements.Count; index++)
                {
                    var descendant = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(descendants.Elements.GetDictionary(index));
                    var descriptor = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(descendant.Elements.GetDictionary("/FontDescriptor"));
                    var binaryFont = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(descriptor.Elements.GetDictionary("/FontFile2"));
                    Assert.NotNull(binaryFont.Stream);
                    Assert.Equal(expectedFont, binaryFont.Stream.UnfilteredValue);
                    supportedStreams.Add(binaryFont);
                    lockedFontStreams.Add(binaryFont);
                }
            }
        }
        foreach (var dictionary in document.Internals.GetAllObjects().OfType<PdfSharp.Pdf.PdfDictionary>())
        {
            // PDFsharp emits catalog XMP itself. Inspect it as XML, while still
            // refusing page/other metadata and every unsupported payload channel.
            Assert.False(
                dictionary.Elements.ContainsKey("/Metadata")
                    && !ReferenceEquals(dictionary, document.Internals.Catalog),
                "Uninspected XMP metadata is not supported by this privacy proof.");
            if (dictionary.Stream is not null)
            {
                Assert.Contains(dictionary, supportedStreams);
                if (ReferenceEquals(dictionary, catalogMetadata))
                {
                    yield return DecodeCatalogXmp(dictionary.Stream.UnfilteredValue);
                }
                else if (!lockedFontStreams.Contains(dictionary))
                {
                    foreach (var surface in DecodedSyntaxStrings(dictionary.Stream.UnfilteredValue))
                    {
                        yield return surface;
                    }
                }
            }
        }
    }

    private static string DecodeCatalogXmp(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 65_536,
            });
            var metadata = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var root = Assert.IsType<XElement>(metadata.Root);
            Assert.Equal(XName.Get("xmpmeta", "adobe:ns:meta/"), root.Name);
            Assert.NotNull(root.Element(
                XName.Get("RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")));
            // Re-serialization exposes decoded character references, text,
            // attributes, comments and processing instructions to the same scan.
            return metadata.ToString(SaveOptions.DisableFormatting)
                + "\n" + string.Join("\n", metadata.DescendantNodes().OfType<XText>().Select(text => text.Value))
                + "\n" + string.Join("\n", metadata.Descendants().Attributes().Select(attribute => attribute.Value));
        }
        catch (XmlException)
        {
            throw new Xunit.Sdk.XunitException("Unparseable XMP metadata cannot be ignored.");
        }
    }

    private static IEnumerable<string> DecodedObjectStrings(PdfSharp.Pdf.PdfItem item)
    {
        switch (item)
        {
            case PdfSharp.Pdf.PdfString text:
                yield return text.Value;
                yield break;
            case PdfSharp.Pdf.PdfStringObject textObject:
                yield return textObject.Value;
                yield break;
            case PdfSharp.Pdf.PdfDictionary dictionary:
                foreach (var key in dictionary.Elements.Keys)
                {
                    if (key is not "/CIDSystemInfo" and not "/CIDToGIDMap")
                    {
                        yield return key;
                    }
                    var itemValue = dictionary.Elements[key];
                    if (itemValue is null)
                    {
                        continue;
                    }
                    if (key == "/Subtype" && itemValue is PdfSharp.Pdf.PdfName { Value: "/CIDFontType2" })
                    {
                        continue;
                    }
                    foreach (var value in DecodedObjectStrings(itemValue))
                    {
                        yield return value;
                    }
                }
                yield break;
            case PdfSharp.Pdf.PdfArray array:
                foreach (var value in array.Elements.Cast<PdfSharp.Pdf.PdfItem>().SelectMany(DecodedObjectStrings))
                {
                    yield return value;
                }
                yield break;
            case PdfSharp.Pdf.PdfName name:
                yield return name.Value;
                yield break;
            case PdfSharp.Pdf.PdfNameObject nameObject:
                yield return nameObject.Value;
                yield break;
            // Every indirect object is visited separately; numeric primitives are structural.
            case PdfSharp.Pdf.Advanced.PdfReference:
            case PdfSharp.Pdf.PdfBoolean:
            case PdfSharp.Pdf.PdfBooleanObject:
            case PdfSharp.Pdf.PdfInteger:
            case PdfSharp.Pdf.PdfIntegerObject:
            case PdfSharp.Pdf.PdfLongInteger:
            case PdfSharp.Pdf.PdfLongIntegerObject:
            case PdfSharp.Pdf.PdfReal:
            case PdfSharp.Pdf.PdfRealObject:
            case PdfSharp.Pdf.PdfRectangle:
            case PdfSharp.Pdf.PdfNull:
            case PdfSharp.Pdf.PdfNullObject:
                yield break;
            default:
                throw new InvalidOperationException($"An uninspected PDF payload cannot be ignored: {item.GetType().FullName}.");
        }
    }

    private static IEnumerable<string> DecodedSyntaxStrings(
        ReadOnlyMemory<byte> content, bool isDocument = false, int depth = 0, IReadOnlySet<string>? reviewedRawStreams = null)
    {
        Assert.InRange(depth, 0, 64);
        using var input = new UglyToad.PdfPig.Core.MemoryInputBytes(content);
        var scanner = new UglyToad.PdfPig.Tokenization.Scanner.CoreTokenScanner(
            input, usePdfDocEncoding: true, new UglyToad.PdfPig.Core.StackDepthGuard(64));
        IToken? previous = null;
        while (scanner.MoveNext())
        {
            var token = scanner.CurrentToken;
            if (isDocument && token is OperatorToken { Data: "stream" })
            {
                // Parse outer comments without interpreting binary stream bytes as PDF tokens.
                // Every stream is separately identified/decoded above; only the vendor-pinned
                // font binary is exempt from payload inspection. Indirect lengths fail closed.
                var dictionary = Assert.IsType<DictionaryToken>(previous);
                var length = Assert.IsType<NumericToken>(dictionary.Data[NameToken.Create("Length")]);
                Assert.False(length.HasDecimalPlaces);
                Assert.True(double.IsFinite(length.Double));
                Assert.InRange(length.Double, 0, content.Length);
                Assert.Equal(Math.Truncate(length.Double), length.Double);
                var start = checked((int)scanner.CurrentTokenStart + "stream".Length);
                if (content.Span[start] == (byte)'\r')
                {
                    start++;
                }
                Assert.Equal((byte)'\n', content.Span[start]);
                start++;
                var finish = checked(start + (int)length.Long);
                Assert.InRange(finish, start, content.Length);
                Assert.NotNull(reviewedRawStreams);
                Assert.Contains(RawStreamIdentity(content.Span.Slice(start, finish - start)), reviewedRawStreams);
                scanner.Seek(finish);
                scanner.ClearPreReadByte();
                Assert.True(scanner.MoveNext());
                Assert.Equal("endstream", Assert.IsType<OperatorToken>(scanner.CurrentToken).Data);
                previous = scanner.CurrentToken;
                continue;
            }
            if (token is ArrayToken or DictionaryToken)
            {
                var start = checked((int)scanner.CurrentTokenStart);
                var finish = checked((int)scanner.CurrentPosition);
                var delimiter = token is ArrayToken ? 1 : 2;
                var opening = token is ArrayToken ? (byte)'[' : (byte)'<';
                var closing = token is ArrayToken ? (byte)']' : (byte)'>';
                Assert.InRange(finish, start + 2 * delimiter, content.Length);
                for (var index = 0; index < delimiter; index++)
                {
                    Assert.Equal(opening, content.Span[start + index]);
                    Assert.Equal(closing, content.Span[finish - 1 - index]);
                }
                // Container tokenizers discard comments. Rescan the bounded original body
                // so comments at every nesting level remain visible to the privacy guard.
                foreach (var value in DecodedSyntaxStrings(
                    content.Slice(start + delimiter, finish - start - 2 * delimiter), depth: depth + 1))
                {
                    yield return value;
                }
            }
            else
            {
                switch (token)
                {
                    case StringToken text:
                        yield return text.Data;
                        break;
                    case HexToken hex:
                        yield return hex.Data;
                        break;
                    case CommentToken comment:
                        yield return comment.Data;
                        break;
                    case NameToken name:
                        // Exact PDF/CMap grammar names, never arbitrary names or comments.
                        if (name.Data is not "CIDInit" and not "CIDSystemInfo" and not "CIDToGIDMap" and not "CIDFontType2")
                        {
                            yield return name.Data;
                        }
                        break;
                    case OperatorToken operation:
                        yield return operation.Data;
                        break;
                    case NumericToken:
                    case BooleanToken:
                    case NullToken:
                    case EndOfLineToken:
                        break;
                    default:
                        throw new InvalidOperationException("An uninspected PDF syntax token cannot be ignored.");
                }
            }
            previous = token;
        }
    }

    private static string RawStreamIdentity(ReadOnlySpan<byte> bytes) =>
        bytes.Length.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(SHA256.HashData(bytes));

    private static IEnumerable<string> DecodedMetadataValues(IToken? token)
    {
        switch (token)
        {
            case null:
            case NullToken:
                yield break;
            case StringToken text:
                yield return text.Data;
                yield break;
            case HexToken hex:
                yield return hex.Data;
                yield break;
            case NameToken name:
                yield return name.Data;
                yield break;
            case DictionaryToken dictionary:
                foreach (var value in dictionary.Data.Values.SelectMany(DecodedMetadataValues))
                {
                    yield return value;
                }
                yield break;
            case ArrayToken array:
                foreach (var value in array.Data.SelectMany(DecodedMetadataValues))
                {
                    yield return value;
                }
                yield break;
            default:
                throw new InvalidOperationException("Unresolved or unsupported PDF metadata cannot be ignored.");
        }
    }

    private static byte[] CreateNonvisiblePrivacyProbe(string channel, string marker)
    {
        var content = CreatePrivacyProbePdf();
        if (channel == "outer-comment")
        {
            return content.Concat(Encoding.ASCII.GetBytes($"\n% {marker}\n")).ToArray();
        }
        if (channel == "outer-unreferenced-stream")
        {
            var payload = Encoding.ASCII.GetBytes(marker);
            var extra = $"\n99999 0 obj\n<< /Length {payload.Length} >>\nstream\n{marker}\nendstream\nendobj\n";
            return content.Concat(Encoding.ASCII.GetBytes(extra)).ToArray();
        }
        using var input = new MemoryStream(content, writable: false);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        if (channel == "catalog-name")
        {
            document.Internals.Catalog.Elements["/LocalHostPayload"] = new PdfSharp.Pdf.PdfName("/" + marker);
        }
        else
        {
            var page = document.Pages[0];
            PdfSharp.Pdf.PdfDictionary stream;
            if (channel.StartsWith("page-", StringComparison.Ordinal))
            {
                stream = Assert.IsAssignableFrom<PdfSharp.Pdf.PdfDictionary>(page.Contents.Elements.GetDictionary(0));
            }
            else
            {
                var fonts = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(page.Resources.Elements.GetDictionary("/Font"));
                var font = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(fonts.Elements.GetDictionary(fonts.Elements.Keys.First()));
                stream = Assert.IsType<PdfSharp.Pdf.PdfDictionary>(font.Elements.GetDictionary("/ToUnicode"));
            }
            var suffix = channel.EndsWith("array-comment", StringComparison.Ordinal)
                ? $"\n[ % {marker}\n ]\n"
                : channel.EndsWith("dictionary-comment", StringComparison.Ordinal)
                    ? $"\n<< % {marker}\n >>\n"
                    : channel.EndsWith("unused-string", StringComparison.Ordinal)
                        ? $"\n({marker})\n"
                        : $"\n% {marker}\n";
            var decoded = stream.Stream.UnfilteredValue;
            stream.Elements.Remove("/Filter");
            stream.Elements.Remove("/DecodeParms");
            stream.Stream.Value = decoded.Concat(Encoding.ASCII.GetBytes(suffix)).ToArray();
        }
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static byte[] CreateExtraPayloadPrivacyProbe(string marker, bool onPage, bool literal)
    {
        using var input = new MemoryStream(CreatePrivacyProbePdf(), writable: false);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        if (literal)
        {
            document.Internals.Catalog.Elements["/LocalHostPayload"] = new PdfSharp.Pdf.PdfString(marker);
        }
        else
        {
            var metadata = new PdfSharp.Pdf.PdfDictionary(document);
            metadata.Elements.SetName("/Type", "/Metadata");
            metadata.Elements.SetName("/Subtype", "/XML");
            metadata.CreateStream(Encoding.UTF8.GetBytes($"<x:xmpmeta xmlns:x='adobe:ns:meta/'>{marker}</x:xmpmeta>"));
            document.Internals.AddObject(metadata);
            PdfSharp.Pdf.PdfDictionary target = onPage ? document.Pages[0] : document.Internals.Catalog;
            target.Elements["/Metadata"] = metadata.Reference!;
        }
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static byte[] CreatePrivacyProbePdf(
        string[]? lines = null, string? field = null, string? value = null, bool unicode = false)
    {
        var artifact = new TechnicalPdfGenerator().Generate(new PdfGenerationRequest(
            Guid.Parse("eb4b6211-2afc-46e1-9a55-1a96c3c06897"),
            CreateFileNameParts(), lines ?? ["Teknik PDF PoC", GoldenTurkishText]));
        if (field is null)
        {
            return artifact.Content;
        }

        using var source = new MemoryStream(artifact.Content, writable: false);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(source, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        document.Info.Elements[field] = unicode
            ? new PdfSharp.Pdf.PdfString(value!, PdfSharp.Pdf.PdfStringEncoding.Unicode)
            : new PdfSharp.Pdf.PdfString(value!);
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static PdfDocumentFileNameParts CreateFileNameParts() => new(
        "Satış Sipariş Sözleşmesi",
        "SPS-2026-01842",
        "ABC Demir",
        new DateOnly(2026, 8, 25),
        1,
        "Onaylı");

    private sealed class CountOnlyReadOnlyList : IReadOnlyList<string>
    {
        public CountOnlyReadOnlyList(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public string this[int index] =>
            throw new InvalidOperationException("The line-count guard must run before element access.");

        public IEnumerator<string> GetEnumerator() =>
            throw new InvalidOperationException("The line-count guard must run before enumeration.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
