using System.Security.Cryptography;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

/// <summary>Verified bytes of one immutable uploaded version; no path, URL, markup or executable template.</summary>
public sealed record DocumentPdfBrandingAsset(Guid DocumentId, int VersionNo, string ContentType, string Sha256,
    ReadOnlyMemory<byte> Content);

/// <summary>The Documents owner's approved publication. Business content remains the producer's closed vocabulary.</summary>
public sealed record DocumentPdfBranding(Guid CompanyId, Guid PublicationId, string TemplateCode, string TemplateVersion,
    DocumentPdfBrandingAsset? Logo, DocumentPdfBrandingAsset? Letterhead);

/// <summary>
/// Existing PDFsharp 6.2.4 stream parsers only. PDF assets are single-page form XObjects, never attached documents or
/// interactive pages. A multi-page asset is explicitly unsupported instead of silently selecting one page.
/// </summary>
public static class DocumentPdfBrandingValidation
{
    public static void Validate(DocumentPdfBrandingAsset asset) { using var image = BrandingImage.Open(asset); }
}

internal sealed class BrandingImage : IDisposable
{
    private readonly MemoryStream stream;
    private readonly XImage image;
    private BrandingImage(MemoryStream stream, XImage image) { this.stream = stream; this.image = image; }

    public static BrandingImage Open(DocumentPdfBrandingAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.DocumentId == Guid.Empty || asset.VersionNo < 1 || asset.Content.IsEmpty
            || asset.Sha256 is not { Length: 64 } || !asset.Sha256.All(char.IsAsciiHexDigit)
            || !string.Equals(Convert.ToHexString(SHA256.HashData(asset.Content.Span)), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Branding asset identity, version or content hash is invalid.");
        var bytes = asset.Content.ToArray();
        var signature = asset.ContentType switch
        {
            "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            _ => false,
        };
        if (!signature) throw new ArgumentException("The branding asset does not match a supported image or PDF content type.");
        var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        XImage? image = null;
        try
        {
            if (asset.ContentType == "application/pdf")
            {
                var form = XPdfForm.FromStream(stream); image = form;
                if (form.PageCount != 1) throw new ArgumentException("A PDF branding asset must have exactly one page.");
            }
            else image = XImage.FromStream(stream);
            if (!double.IsFinite(image.PointWidth) || !double.IsFinite(image.PointHeight) || image.PointWidth <= 0 || image.PointHeight <= 0)
                throw new ArgumentException("The branding asset has no usable visual dimensions.");
            return new(stream, image);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException
            or NotSupportedException or IOException or PdfSharp.Pdf.IO.PdfReaderException)
        { image?.Dispose(); stream.Dispose(); throw new ArgumentException("The branding asset cannot be rendered as a fixed image.", nameof(asset), error); }
        catch { image?.Dispose(); stream.Dispose(); throw; }
    }

    public void DrawFit(XGraphics graphics, double x, double y, double width, double height)
    {
        var scale = Math.Min(width / image.PointWidth, height / image.PointHeight);
        var drawnWidth = image.PointWidth * scale; var drawnHeight = image.PointHeight * scale;
        graphics.DrawImage(image, x + (width - drawnWidth) / 2, y + (height - drawnHeight) / 2, drawnWidth, drawnHeight);
    }
    public void Dispose() { image.Dispose(); stream.Dispose(); }
}

internal sealed class DocumentPdfBrandingImages : IDisposable
{
    private readonly BrandingImage? logo;
    private readonly BrandingImage? letterhead;
    public double HeaderReservePoints => logo is null ? 0 : 60;
    private DocumentPdfBrandingImages(BrandingImage? logo, BrandingImage? letterhead) { this.logo = logo; this.letterhead = letterhead; }

    public static DocumentPdfBrandingImages? Open(DocumentPdfBranding? branding, PdfDocument document)
    {
        if (branding is null) return null;
        if (branding.CompanyId == Guid.Empty || branding.PublicationId == Guid.Empty || string.IsNullOrWhiteSpace(branding.TemplateCode)
            || string.IsNullOrWhiteSpace(branding.TemplateVersion) || branding.Logo is null && branding.Letterhead is null)
            throw new ArgumentException("An approved corporate template needs a publication and an actual asset.");
        BrandingImage? logo = null; BrandingImage? letterhead = null;
        try
        {
            if (branding.Logo is not null) logo = BrandingImage.Open(branding.Logo);
            if (branding.Letterhead is not null) letterhead = BrandingImage.Open(branding.Letterhead);
            // This immutable identity snapshot travels with the PDF bytes even for read-only, non-vault producers.
            document.Info.Elements.SetString("/GoymenTemplatePublication", JsonSerializer.Serialize(new
            {
                branding.CompanyId, branding.PublicationId, branding.TemplateCode, branding.TemplateVersion,
                logo = Snapshot(branding.Logo), letterhead = Snapshot(branding.Letterhead),
            }));
            return new(logo, letterhead);
        }
        catch { logo?.Dispose(); letterhead?.Dispose(); throw; }
    }

    private static object? Snapshot(DocumentPdfBrandingAsset? asset) => asset is null ? null
        : new { asset.DocumentId, asset.VersionNo, asset.ContentType, asset.Sha256 };
    public void Draw(XGraphics graphics, double pageWidth, double pageHeight, double margin)
    {
        letterhead?.DrawFit(graphics, 0, 0, pageWidth, pageHeight);
        logo?.DrawFit(graphics, margin, margin, Math.Min(180, pageWidth - 2 * margin), HeaderReservePoints - 8);
    }
    public void Dispose() { logo?.Dispose(); letterhead?.Dispose(); }
}
