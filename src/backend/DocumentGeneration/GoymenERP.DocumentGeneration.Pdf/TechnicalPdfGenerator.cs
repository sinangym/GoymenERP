using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

public sealed class TechnicalPdfGenerator
{
    public const string PdfMediaType = "application/pdf";

    public GeneratedPdf Generate(PdfGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TechnicalId == Guid.Empty)
        {
            throw new ArgumentException("The technical document identifier cannot be empty.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Lines);
        if (request.Lines.Any(static line => line is null))
        {
            throw new ArgumentException("PDF text lines cannot contain null values.", nameof(request));
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A technical PDF must contain at least one text line.", nameof(request));
        }

        var visibleFileName = PdfVisibleFileName.Build(request.FileNameParts);
        var normalizedLines = request.Lines
            .Select(static line => line.Normalize(NormalizationForm.FormC))
            .ToArray();
        NotoSansRegularFont.EnsureIntegrity();
        PdfFontResolverRegistration.EnsureRegistered();

        using var document = new PdfDocument();
        document.Info.Title = "Teknik PDF PoC";
        document.Info.Subject = "Türkçe Unicode ve gömülü font semantik golden";

        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var font = new XFont(NotoSansRegularFont.FamilyName, 12, XFontStyleEx.Regular);
            const double left = 48;
            const double top = 48;
            const double lineHeight = 22;
            var maximumLineCount = (int)Math.Floor((page.Height.Point - (top * 2)) / lineHeight) + 1;
            if (normalizedLines.Length > maximumLineCount)
            {
                throw new ArgumentException(
                    "The requested text does not fit on the single-page technical PDF.",
                    nameof(request));
            }

            for (var index = 0; index < normalizedLines.Length; index++)
            {
                var line = normalizedLines[index];
                if (graphics.MeasureString(line, font).Width > page.Width.Point - (left * 2))
                {
                    throw new ArgumentException(
                        "A requested text line is wider than the single-page technical PDF content area.",
                        nameof(request));
                }

                graphics.DrawString(
                    line,
                    font,
                    XBrushes.Black,
                    new XPoint(left, top + (index * lineHeight)));
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
