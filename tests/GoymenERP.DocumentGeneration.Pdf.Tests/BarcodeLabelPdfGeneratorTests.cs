using System.Text;
using GoymenERP.DocumentGeneration.Pdf;
using UglyToad.PdfPig;

namespace GoymenERP.DocumentGeneration.Pdf.Tests;

public sealed class BarcodeLabelPdfGeneratorTests
{
    [Fact]
    public void The_label_pdf_preserves_turkish_text_and_the_library_renders_the_exact_code_without_a_logo()
    {
        var pdf = new BarcodeLabelPdfGenerator().Generate(Request());
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf.Content.AsSpan(0, 5)));
        using var read = PdfDocument.Open(pdf.Content);
        var text = Assert.Single(read.GetPages()).Text;
        Assert.Contains("SENTETIK-001", text, StringComparison.Ordinal);
        Assert.Contains("Döküm", text, StringComparison.Ordinal);
        Assert.Contains("İĞÜŞÖÇ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_rendering_of_the_same_transport_and_identity_has_identical_vault_content()
    {
        var generator = new BarcodeLabelPdfGenerator();
        Assert.Equal(generator.Generate(Request()).Content, generator.Generate(Request()).Content);
    }

    [Theory]
    [InlineData("SENTETIK-001", true)]
    [InlineData("SENTETIK/01 + 2", true)]
    [InlineData("sentetik-001", false)]
    [InlineData("İĞÜŞÖÇ", false)]
    [InlineData("CODE*STOP", false)]
    [InlineData("'", false)]
    public void The_exact_library_alphabet_is_checked_before_rendering_and_never_transliterated(string code, bool accepted)
    {
        Assert.Equal(accepted, BarcodeLabelPdfGenerator.CanEncode(code));
        if (!accepted) Assert.Throws<ArgumentException>(() => new BarcodeLabelPdfGenerator().Generate(Request() with { BarcodeText = code }));
    }

    private static BarcodeLabelPdfRequest Request() => new(Guid.Parse("a9d42d1e-d92d-4e12-a83a-d34ba34cb753"),
        "Etiket_SENTETIK-001.pdf", DateTimeOffset.Parse("2026-11-16T09:00:00Z"), "Malzeme etiketi", "SENTETIK-001",
        ["Etiket kodu: SENTETIK-001", "Döküm numarası: İĞÜŞÖÇ", "Rulo numarası: Kaydedilmemiş"]);
}
