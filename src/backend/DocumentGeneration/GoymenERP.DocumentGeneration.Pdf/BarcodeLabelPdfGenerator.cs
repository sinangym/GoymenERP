using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.BarCodes;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

public sealed record BarcodeLabelPdfRequest(Guid TechnicalId, string VisibleFileName, DateTimeOffset RequestedAtUtc,
    string Title, string BarcodeText, IReadOnlyList<string> Lines);

/// <summary>
/// Brand-free label PDF over the existing PDFsharp 6.2.4 Code3of9Standard and vendored Noto Sans.
/// ASSUMPTION-STK-BARCODE-1: Code39 is a reversible print choice; the exact stored code is encoded, never uppercased.
/// Source API: PDFsharp v6.2.4 PdfSharp.BarCodes/Drawing.BarCodes/Code3of9Standard.cs (existing MIT package).
/// Request timestamps and PDF identifiers are stable for the vault's same-key replay; no current clock is read here.
/// </summary>
public sealed class BarcodeLabelPdfGenerator
{
    private const string BarcodeAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";
    private const int MaximumCodeLength = 64;
    private const int MaximumLineCount = 8;
    private const int MaximumTextLength = 512;
    private const double Margin = 48;
    private const double LineHeight = 24;
    private const double BarcodeHeight = 90;

    public static bool CanEncode(string? code) => code is { Length: > 0 and <= MaximumCodeLength }
        && code.All(character => BarcodeAlphabet.Contains(character, StringComparison.Ordinal));

    public GeneratedPdf Generate(BarcodeLabelPdfRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TechnicalId == Guid.Empty || request.RequestedAtUtc.Offset != TimeSpan.Zero || !CanEncode(request.BarcodeText)
            || request.Lines is null || request.Lines.Count is < 1 or > MaximumLineCount)
            throw new ArgumentException("The barcode label PDF request is incomplete or cannot be encoded.", nameof(request));
        var text = new[] { request.Title }.Concat(request.Lines).Select(value =>
            string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength || value.Any(char.IsControl)
                ? throw new ArgumentException("A label PDF text is missing, too long or carries controls.", nameof(request))
                : value.Normalize(NormalizationForm.FormC)).ToArray();
        NotoSansRegularFont.EnsureIntegrity();
        PdfFontResolverRegistration.EnsureRegistered();
        using var document = new PdfDocument();
        document.Info.Title = text[0];
        document.Info.CreationDate = request.RequestedAtUtc.UtcDateTime;
        document.Info.ModificationDate = request.RequestedAtUtc.UtcDateTime;
        var identifier = Encoding.Latin1.GetString(request.TechnicalId.ToByteArray());
        document.Internals.FirstDocumentID = identifier;
        document.Internals.SecondDocumentID = identifier;
        var page = document.AddPage();
        page.Orientation = PageOrientation.Landscape;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var font = new XFont(NotoSansRegularFont.FamilyName, 12, XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));
            var available = page.Width.Point - 2 * Margin;
            for (var index = 0; index < text.Length; index++)
            {
                if (graphics.MeasureString(text[index], font).Width > available)
                    throw new ArgumentException("The label text does not fit the PDF content area.", nameof(request));
                graphics.DrawString(text[index], font, XBrushes.Black, new XPoint(Margin, Margin + index * LineHeight));
            }
            // The page margin provides the white quiet zone around the library-rendered barcode.
            var barcode = new Code3of9Standard(request.BarcodeText, new XSize(available - 32, BarcodeHeight))
            { TextLocation = TextLocation.None, Anchor = AnchorType.MiddleCenter };
            var top = Margin + text.Length * LineHeight + 16;
            graphics.DrawBarCode(barcode, XBrushes.Black, new XPoint(page.Width.Point / 2, top + BarcodeHeight / 2));
            graphics.DrawString(request.BarcodeText, font, XBrushes.Black,
                new XPoint(Margin, top + BarcodeHeight + LineHeight));
        }
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return new(request.TechnicalId, PdfVisibleFileName.Normalize(request.VisibleFileName),
            TechnicalPdfGenerator.PdfMediaType, output.ToArray());
    }
}
