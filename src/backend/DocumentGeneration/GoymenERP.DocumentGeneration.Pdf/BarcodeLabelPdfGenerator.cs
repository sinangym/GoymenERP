using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.BarCodes;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

public sealed record BarcodeLabelPdfRequest(Guid TechnicalId, string VisibleFileName, DateTimeOffset RequestedAtUtc,
    string Title, string BarcodeText, IReadOnlyList<string> Lines, DocumentPdfBranding? Branding = null);

/// <summary>
/// Label PDF over the existing PDFsharp 6.2.4 Code3of9Standard and vendored Noto Sans; an optional fixed corporate
/// publication draws only approved assets, leaving the original technical layout and replay unchanged when absent.
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
        using var branding = DocumentPdfBrandingImages.Open(request.Branding, document);
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
            branding?.Draw(graphics, page.Width.Point, page.Height.Point, Margin);
            var contentTop = Margin + (branding?.HeaderReservePoints ?? 0);
            var font = new XFont(NotoSansRegularFont.FamilyName, 12, XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));
            var available = page.Width.Point - 2 * Margin;
            for (var index = 0; index < text.Length; index++)
            {
                if (graphics.MeasureString(text[index], font).Width > available)
                    throw new ArgumentException("The label text does not fit the PDF content area.", nameof(request));
                graphics.DrawString(text[index], font, XBrushes.Black, new XPoint(Margin, contentTop + index * LineHeight));
            }
            // The page margin provides the white quiet zone around the library-rendered barcode.
            var barcode = new Code3of9Standard(request.BarcodeText, new XSize(available - 32, BarcodeHeight))
            { TextLocation = TextLocation.None, Anchor = AnchorType.MiddleCenter };
            var top = contentTop + text.Length * LineHeight + 16;
            graphics.DrawBarCode(barcode, XBrushes.Black, new XPoint(page.Width.Point / 2, top + BarcodeHeight / 2));
            graphics.DrawString(request.BarcodeText, font, XBrushes.Black,
                new XPoint(Margin, top + BarcodeHeight + LineHeight));
        }
        UseCompleteFontName(page);
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        var content = output.ToArray();
        UseTechnicalMetadataIds(document, content, request.TechnicalId);
        return new(request.TechnicalId, PdfVisibleFileName.Normalize(request.VisibleFileName),
            TechnicalPdfGenerator.PdfMediaType, content);
    }

    private static void UseTechnicalMetadataIds(PdfDocument document, byte[] content, Guid technicalId)
    {
        // PDFsharp 6.2.4 creates XMP with two fresh UUIDs inside Save, after public customization ends.
        // Bind only the generated catalog stream's UUID values. Equal-length writes preserve all PDF offsets.
        var metadata = document.Internals.Catalog.Elements.GetDictionary("/Metadata");
        if (metadata is null || metadata.Elements.GetName("/Type") != "/Metadata"
            || metadata.Elements.GetName("/Subtype") != "/XML" || metadata.Stream is null
            || metadata.Elements.ContainsKey("/Filter"))
            throw new InvalidOperationException("The label PDF catalog metadata is not the expected plain XML stream.");
        var objectId = PdfSharp.Pdf.Advanced.PdfInternals.GetObjectID(metadata);
        if (objectId.IsEmpty)
            throw new InvalidOperationException("The label PDF catalog metadata must be an indirect object.");
        var source = metadata.Stream.Value;
        var start = content.AsSpan().IndexOf(source);
        var objectHeader = Encoding.ASCII.GetBytes($"\n{objectId.ObjectNumber} {objectId.GenerationNumber} obj\n");
        var objectStart = content.AsSpan().IndexOf(objectHeader);
        if (source.Length == 0 || start < 0 || content.AsSpan(start + source.Length).IndexOf(source) >= 0
            || objectStart < 0 || content.AsSpan(objectStart + objectHeader.Length).IndexOf(objectHeader) >= 0
            || start <= objectStart + objectHeader.Length || start - objectStart > 1024
            || !content.AsSpan(start - 7, 7).SequenceEqual("stream\n"u8)
            || !content.AsSpan(start + source.Length).StartsWith("\nendstream\nendobj\n"u8)
            || metadata.Elements.GetInteger("/Length") != source.Length)
            throw new InvalidOperationException("The label PDF catalog metadata object boundaries are inconsistent.");
        var section = "<rdf:Description rdf:about=\"\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\">"u8;
        var sectionStart = source.AsSpan().IndexOf(section);
        if (sectionStart < 0 || source.AsSpan(sectionStart + section.Length).IndexOf(section) >= 0)
            throw new InvalidOperationException("The label PDF catalog XMP identity section is ambiguous.");
        var sectionEndOffset = source.AsSpan(sectionStart).IndexOf("</rdf:Description>"u8);
        if (sectionEndOffset < 0)
            throw new InvalidOperationException("The label PDF catalog XMP identity section is incomplete.");
        var identities = source.AsSpan(sectionStart, sectionEndOffset);
        var stable = Encoding.ASCII.GetBytes(technicalId.ToString("D"));
        foreach (var name in new[] { "DocumentID", "InstanceID" })
        {
            var prefix = Encoding.ASCII.GetBytes($"<xmpMM:{name}>uuid:");
            var suffix = Encoding.ASCII.GetBytes($"</xmpMM:{name}>");
            var field = identities.IndexOf(prefix);
            var value = field + prefix.Length;
            if (field < 0 || identities[(field + prefix.Length)..].IndexOf(prefix) >= 0
                || identities.Length - value < stable.Length + suffix.Length
                || !identities.Slice(value + stable.Length, suffix.Length).SequenceEqual(suffix)
                || !Guid.TryParseExact(Encoding.ASCII.GetString(identities.Slice(value, stable.Length)), "D", out _))
                throw new InvalidOperationException("The label PDF catalog XMP identity is not one UUID value.");
            stable.CopyTo(content.AsSpan(start + sectionStart + value, stable.Length));
        }
    }

    private static void UseCompleteFontName(PdfPage page)
    {
        // PDFsharp 6.2.4 assigns a random subset tag even to EmbedCompleteFontFile fonts. This renderer uses
        // one complete, hash-locked Noto Sans font. Keep its actual name in the three linked font dictionaries.
        // Only this page's own font is touched: imported branding form resources and their subsets are unchanged.
        var fonts = page.Resources.Elements.GetDictionary("/Font");
        if (fonts is null || fonts.Elements.Count != 1)
            throw new InvalidOperationException("The label PDF must contain its one complete Unicode font.");
        var font = fonts.Elements.GetDictionary(fonts.Elements.Keys.Single());
        var descendants = font?.Elements.GetArray("/DescendantFonts");
        var cid = descendants is { Elements.Count: 1 } ? descendants.Elements.GetDictionary(0) : null;
        var descriptor = cid?.Elements.GetDictionary("/FontDescriptor");
        var taggedName = font?.Elements.GetName("/BaseFont");
        if (font is null || cid is null || descriptor is null || font.Elements.GetName("/Subtype") != "/Type0"
            || cid.Elements.GetName("/Subtype") != "/CIDFontType2"
            || taggedName is not { Length: > 8 } || taggedName[0] != '/' || taggedName[7] != '+'
            || !taggedName.Skip(1).Take(6).All(character => character is >= 'A' and <= 'Z')
            || cid.Elements.GetName("/BaseFont") != taggedName || descriptor.Elements.GetName("/FontName") != taggedName)
            throw new InvalidOperationException("The label PDF complete font dictionaries are inconsistent.");
        var completeName = "/" + taggedName[8..];
        font.Elements.SetName("/BaseFont", completeName);
        cid.Elements.SetName("/BaseFont", completeName);
        descriptor.Elements.SetName("/FontName", completeName);
    }
}
