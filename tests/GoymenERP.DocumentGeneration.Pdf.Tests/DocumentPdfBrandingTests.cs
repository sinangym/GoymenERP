using System.Security.Cryptography;
using System.Text;
using GoymenERP.DocumentGeneration.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace GoymenERP.DocumentGeneration.Pdf.Tests;

public sealed class DocumentPdfBrandingTests
{
    [Fact]
    public void An_actual_fixed_pdf_asset_is_a_form_on_every_page_and_preserves_all_business_rows_and_publication_identity()
    {
        var branding = Branding();
        var rows = Enumerable.Range(1, 100).Select(i => (IReadOnlyList<string>)new[] { "SENTETIK-" + i }).ToArray();
        var request = new DocumentPdfRequest(Guid.NewGuid(), new("SENTETIK", "1", "SENTETIK", new(2026, 11, 17), 1, "SENTETIK"),
            "SENTETIK Türkçe İĞÜŞÖÇ", [new DocumentPdfTable([new("Satırlar", 1)], rows)], "SENTETIK", "Sayfa {0} / {1}", Branding: branding);
        var result = new DocumentPdfGenerator().Generate(request);
        using var pdf = PdfReader.Open(new MemoryStream(result.Content), PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
        Assert.Contains(branding.PublicationId.ToString("D"), pdf.Info.Elements.GetString("/GoymenTemplatePublication"), StringComparison.Ordinal);
        Assert.Contains(branding.Logo!.Sha256, pdf.Info.Elements.GetString("/GoymenTemplatePublication"), StringComparison.Ordinal);
        Assert.All(pdf.Pages.Cast<PdfPage>(), page => Assert.NotNull(page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject")));
        using var text = UglyToad.PdfPig.PdfDocument.Open(result.Content);
        var all = string.Concat(text.GetPages().Select(x => x.Text));
        foreach (var row in rows) Assert.Contains(row[0], all, StringComparison.Ordinal);
        Assert.Contains("İĞÜŞÖÇ", all, StringComparison.Ordinal);
        Assert.DoesNotContain("/JavaScript", Encoding.Latin1.GetString(result.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corporate_barcode_request_keeps_the_exact_same_content_on_a_lost_answer_retry()
    {
        var request = new BarcodeLabelPdfRequest(Guid.Parse("58000000-0000-4000-8000-000000000003"), "SENTETIK.pdf",
            DateTimeOffset.Parse("2026-11-17T08:00:00Z"), "SENTETIK İĞÜŞÖÇ", "SENTETIK-001", ["SENTETIK Döküm"], Branding());
        var renderer = new BarcodeLabelPdfGenerator();
        Assert.Equal(renderer.Generate(request).Content, renderer.Generate(request).Content);
    }

    [Fact]
    public void Stable_barcode_rendering_preserves_the_imported_branding_font_name_and_text()
    {
        var logo = new TechnicalPdfGenerator().Generate(new PdfGenerationRequest(Guid.NewGuid(),
            new("SENTETIK", "1", "SENTETIK", new(2026, 11, 17), 1, "SENTETIK"), ["SENTETIK logo İĞÜŞÖÇ"]));
        using var original = PdfReader.Open(new MemoryStream(logo.Content), PdfDocumentOpenMode.Import);
        var originalFontName = OnlyFont(original.Pages[0].Resources).Elements.GetName("/BaseFont");
        Assert.Matches(@"^/[A-Z]{6}\+", originalFontName);
        var request = new BarcodeLabelPdfRequest(Guid.Parse("58000000-0000-4000-8000-000000000003"), "SENTETIK.pdf",
            DateTimeOffset.Parse("2026-11-17T08:00:00Z"), "SENTETIK etiket", "SENTETIK-001", ["SENTETIK Döküm"],
            Branding() with { Logo = Asset(logo.Content, "application/pdf") });
        var renderer = new BarcodeLabelPdfGenerator();
        var result = renderer.Generate(request);
        Assert.Equal(result.Content, renderer.Generate(request).Content);

        using var pdf = PdfReader.Open(new MemoryStream(result.Content), PdfDocumentOpenMode.Import);
        var xObjects = Assert.IsType<PdfDictionary>(pdf.Pages[0].Resources.Elements.GetDictionary("/XObject"));
        var form = Assert.IsType<PdfDictionary>(xObjects.Elements.GetDictionary(Assert.Single(xObjects.Elements.Keys)));
        var resources = Assert.IsType<PdfDictionary>(form.Elements.GetDictionary("/Resources"));
        Assert.Equal(originalFontName, OnlyFont(resources).Elements.GetName("/BaseFont"));
        using var read = UglyToad.PdfPig.PdfDocument.Open(result.Content);
        Assert.Contains("SENTETIK logo İĞÜŞÖÇ", Assert.Single(read.GetPages()).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_hash_unknown_markup_malformed_image_and_multi_page_assets_cannot_be_rendered_as_a_template()
    {
        var asset = Branding().Logo!;
        Assert.Throws<ArgumentException>(() => DocumentPdfBrandingValidation.Validate(asset with { Sha256 = new string('a', 64) }));
        var html = "<svg><script/></svg>"u8.ToArray();
        Assert.Throws<ArgumentException>(() => DocumentPdfBrandingValidation.Validate(Asset(html, "image/svg+xml")));
        Assert.Throws<ArgumentException>(() => DocumentPdfBrandingValidation.Validate(Asset("%PDF-broken"u8.ToArray(), "application/pdf")));
        Assert.Throws<ArgumentException>(() => DocumentPdfBrandingValidation.Validate(Asset(PdfAsset(2), "application/pdf")));
    }

    private static DocumentPdfBranding Branding() => new(Guid.Parse("58000000-0000-4000-8000-000000000001"),
        Guid.Parse("58000000-0000-4000-8000-000000000002"), "inventory.material-label.technical", "pub.1", Asset(PdfAsset(1), "application/pdf"), null);
    private static DocumentPdfBrandingAsset Asset(byte[] bytes, string type) => new(Guid.Parse("58000000-0000-4000-8000-000000000004"),
        1, type, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes);
    private static PdfDictionary OnlyFont(PdfDictionary resources)
    {
        var fonts = Assert.IsType<PdfDictionary>(resources.Elements.GetDictionary("/Font"));
        return Assert.IsType<PdfDictionary>(fonts.Elements.GetDictionary(Assert.Single(fonts.Elements.Keys)));
    }
    private static byte[] PdfAsset(int pages)
    {
        using var doc = new PdfDocument();
        doc.Info.CreationDate = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
        doc.Info.ModificationDate = doc.Info.CreationDate;
        for (var i = 0; i < pages; i++) { var page = doc.AddPage(); using var graphics = XGraphics.FromPdfPage(page); graphics.DrawRectangle(XBrushes.Blue, 0, 0, 80, 30); }
        using var stream = new MemoryStream(); doc.Save(stream, closeStream: false); return stream.ToArray();
    }
}
