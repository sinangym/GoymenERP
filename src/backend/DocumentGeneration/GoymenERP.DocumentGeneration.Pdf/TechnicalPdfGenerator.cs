using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

public sealed class TechnicalPdfGenerator
{
    public const string PdfMediaType = "application/pdf";

    private const double LeftMarginPoints = 48;
    private const double TopMarginPoints = 48;
    private const double LineHeightPoints = 22;

    // The rendered A4 line is far shorter; this leaves generous combining-mark headroom
    // while bounding normalization and font-shaping work for every accepted request.
    private const int MaximumRawLineUnits = 4_096;

    public GeneratedPdf Generate(PdfGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TechnicalId == Guid.Empty)
        {
            throw new ArgumentException("The technical document identifier cannot be empty.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Lines);
        var lineCount = request.Lines.Count;
        if (lineCount == 0)
        {
            throw new ArgumentException("A technical PDF must contain at least one text line.", nameof(request));
        }

        using var document = new PdfDocument();
        var page = document.AddPage();
        var maximumLineCount = (int)Math.Floor(
            (page.Height.Point - (TopMarginPoints * 2)) / LineHeightPoints) + 1;
        if (lineCount > maximumLineCount)
        {
            throw new ArgumentException(
                "The requested text does not fit on the single-page technical PDF.",
                nameof(request));
        }

        var normalizedLines = new string[lineCount];
        for (var index = 0; index < lineCount; index++)
        {
            var line = request.Lines[index];
            if (line is null)
            {
                throw new ArgumentException("PDF text lines cannot contain null values.", nameof(request));
            }

            if (line.Length > MaximumRawLineUnits)
            {
                throw new ArgumentException(
                    "A PDF text line exceeds the 4096-unit raw input limit.",
                    nameof(request));
            }

            normalizedLines[index] = line.Normalize(NormalizationForm.FormC);
        }

        var visibleFileName = PdfVisibleFileName.Build(request.FileNameParts);
        NotoSansRegularFont.EnsureIntegrity();
        PdfFontResolverRegistration.EnsureRegistered();

        document.Info.Title = "Teknik PDF PoC";
        document.Info.Subject = "Türkçe Unicode ve gömülü font semantik golden";

        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var font = new XFont(
                NotoSansRegularFont.FamilyName,
                12,
                XFontStyleEx.Regular,
                new XPdfFontOptions(
                    PdfFontEncoding.Unicode,
                    PdfFontEmbedding.EmbedCompleteFontFile));
            for (var index = 0; index < normalizedLines.Length; index++)
            {
                var line = normalizedLines[index];
                if (graphics.MeasureString(line, font).Width > page.Width.Point - (LeftMarginPoints * 2))
                {
                    throw new ArgumentException(
                        "A requested text line is wider than the single-page technical PDF content area.",
                        nameof(request));
                }

                graphics.DrawString(
                    line,
                    font,
                    XBrushes.Black,
                    new XPoint(LeftMarginPoints, TopMarginPoints + (index * LineHeightPoints)));
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);

        return new GeneratedPdf(
            request.TechnicalId,
            visibleFileName,
            PdfMediaType,
            output.ToArray());
    }
}
