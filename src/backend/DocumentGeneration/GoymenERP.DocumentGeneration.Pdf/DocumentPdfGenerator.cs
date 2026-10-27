using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace GoymenERP.DocumentGeneration.Pdf;

/// <summary>
/// Lays a <see cref="DocumentPdfRequest"/> out on A4 landscape pages with the vendored, fully embedded Noto Sans (the
/// DEC-DOC-010 technical fallback: no logo, letterhead or company template). Text is measured and wrapped, never cut:
/// a word wider than its column breaks between text elements, and a row or field taller than a page is refused instead
/// of being clipped. Every input is bounded before any shaping work; the page count is bounded while laying out.
/// </summary>
public sealed class DocumentPdfGenerator
{
    private const double PageWidthMillimeters = 297;
    private const double PageHeightMillimeters = 210;
    private const double MarginPoints = 36;
    private const double FooterReservePoints = 22;
    private const double TitleFontSize = 15;
    private const double HeadingFontSize = 11.5;
    private const double BodyFontSize = 9;
    private const double FooterFontSize = 7.5;
    private const double WatermarkFontSize = 120;
    private const double WatermarkAngleDegrees = -30;
    private const int WatermarkAlpha = 56;
    private const int WatermarkGray = 128;
    private const double WatermarkMaximumWidthShare = 0.9;
    private const double LineHeightFactor = 1.32;
    private const double CellPaddingPoints = 3;
    private const double BlockGapPoints = 8;
    private const double ColumnGapPoints = 8;
    private const double FieldLabelShare = 0.24;
    private const double RuleWidthPoints = 0.5;
    private const int HeadingKeepWithLines = 3;

    // Technical bounds on the work one request may cause; none is a business limit.
    private const int MaximumRawTextUnits = 4_096;
    private const int MaximumBlockCount = 1_000;
    private const int MaximumFieldCount = 200;
    private const int MaximumColumnCount = 16;
    private const int MaximumRowCount = 5_000;
    private const int MaximumPageCount = 250;

    public GeneratedPdf Generate(DocumentPdfRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TechnicalId == Guid.Empty)
        {
            throw new ArgumentException("The technical document identifier cannot be empty.", nameof(request));
        }

        var title = Text(request.Title, nameof(request.Title));
        if (title.Length == 0)
        {
            throw new ArgumentException("A document PDF needs a title.", nameof(request));
        }

        var footer = Text(request.FooterText, nameof(request.FooterText));
        var pageLabelFormat = PageLabelFormat(request.PageLabelFormat);
        var watermark = request.Watermark is null ? null : Text(request.Watermark, nameof(request.Watermark));
        if (watermark is { Length: 0 })
        {
            throw new ArgumentException("A watermark, when given, cannot be empty.", nameof(request));
        }

        var blocks = Blocks(request.Blocks);
        var visibleFileName = PdfVisibleFileName.Build(request.FileNameParts);
        NotoSansRegularFont.EnsureIntegrity();
        PdfFontResolverRegistration.EnsureRegistered();

        using var document = new PdfDocument();
        document.Info.Title = title;
        var fonts = new Fonts();
        var pages = new List<PdfPage>();
        using (var layout = new Layout(document, pages, fonts))
        {
            layout.Title(title);
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case DocumentPdfHeading heading:
                        layout.Heading(heading.Text);
                        break;
                    case DocumentPdfParagraph paragraph:
                        layout.Paragraph(paragraph.Text);
                        break;
                    case DocumentPdfFields fields:
                        layout.Fields(fields.Fields);
                        break;
                    case DocumentPdfTable table:
                        layout.Table(table);
                        break;
                }
            }
        }

        if (watermark is not null)
        {
            DrawWatermarks(pages, fonts, watermark);
        }

        DrawFooters(pages, fonts, footer, pageLabelFormat);

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return new GeneratedPdf(request.TechnicalId, visibleFileName, TechnicalPdfGenerator.PdfMediaType, output.ToArray());
    }

    /// <summary>The watermark behind every page's content (prepended), centred and diagonal, translucent gray.</summary>
    private static void DrawWatermarks(List<PdfPage> pages, Fonts fonts, string watermark)
    {
        var brush = new XSolidBrush(XColor.FromArgb(WatermarkAlpha, WatermarkGray, WatermarkGray, WatermarkGray));
        foreach (var page in pages)
        {
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
            if (graphics.MeasureString(watermark, fonts.Watermark).Width > page.Width.Point * WatermarkMaximumWidthShare)
            {
                throw new ArgumentException("The watermark is wider than the page.", nameof(watermark));
            }

            var center = new XPoint(page.Width.Point / 2, page.Height.Point / 2);
            graphics.RotateAtTransform(WatermarkAngleDegrees, center);
            graphics.DrawString(watermark, fonts.Watermark, brush, center, XStringFormats.Center);
        }
    }

    private static void DrawFooters(List<PdfPage> pages, Fonts fonts, string footer, string pageLabelFormat)
    {
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var label = string.Format(CultureInfo.InvariantCulture, pageLabelFormat, index + 1, pages.Count);
            var right = page.Width.Point - MarginPoints;
            var labelWidth = graphics.MeasureString(label, fonts.Footer).Width;
            var footerWidth = graphics.MeasureString(footer, fonts.Footer).Width;
            if (footerWidth + ColumnGapPoints + labelWidth > right - MarginPoints)
            {
                throw new ArgumentException("The footer text does not fit on one footer line.", nameof(footer));
            }

            var ruleY = page.Height.Point - MarginPoints - FooterReservePoints + (FooterReservePoints / 3);
            graphics.DrawLine(new XPen(XColors.Gray, RuleWidthPoints), MarginPoints, ruleY, right, ruleY);
            var baseline = ruleY + FooterFontSize + 4;
            if (footer.Length > 0)
            {
                graphics.DrawString(footer, fonts.Footer, XBrushes.Black, new XPoint(MarginPoints, baseline));
            }

            graphics.DrawString(label, fonts.Footer, XBrushes.Black, new XPoint(right - labelWidth, baseline));
        }
    }

    private static string PageLabelFormat(string? format)
    {
        var text = Text(format, nameof(DocumentPdfRequest.PageLabelFormat));
        if (!text.Contains("{0}", StringComparison.Ordinal) || !text.Contains("{1}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The page label format must name the page {0} and the page count {1}.", nameof(format));
        }

        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, 1, 1);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The page label format is not a valid composite format.", nameof(format), exception);
        }

        return text;
    }

    private static List<DocumentPdfBlock> Blocks(IReadOnlyList<DocumentPdfBlock>? blocks)
    {
        if (blocks is null)
        {
            throw new ArgumentException("A document PDF needs its block list.", nameof(blocks));
        }

        if (blocks.Count > MaximumBlockCount)
        {
            throw new ArgumentException($"A document PDF accepts at most {MaximumBlockCount} blocks.", nameof(blocks));
        }

        var result = new List<DocumentPdfBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            result.Add(block switch
            {
                DocumentPdfHeading heading => new DocumentPdfHeading(Text(heading.Text, nameof(DocumentPdfHeading))),
                DocumentPdfParagraph paragraph => new DocumentPdfParagraph(Text(paragraph.Text, nameof(DocumentPdfParagraph))),
                DocumentPdfFields fields => Fields(fields),
                DocumentPdfTable table => Table(table),
                _ => throw new ArgumentException("A document PDF block must be one of the known block kinds.", nameof(blocks)),
            });
        }

        return result;
    }

    private static DocumentPdfFields Fields(DocumentPdfFields fields)
    {
        if (fields.Fields is null || fields.Fields.Count is 0 or > MaximumFieldCount)
        {
            throw new ArgumentException($"A field block holds 1 to {MaximumFieldCount} fields.", nameof(fields));
        }

        return new DocumentPdfFields([.. fields.Fields.Select(field => field is null
            ? throw new ArgumentException("A field cannot be null.", nameof(fields))
            : new DocumentPdfField(Text(field.Label, nameof(field.Label)), Text(field.Value, nameof(field.Value))))]);
    }

    private static DocumentPdfTable Table(DocumentPdfTable table)
    {
        if (table.Columns is null || table.Columns.Count is 0 or > MaximumColumnCount)
        {
            throw new ArgumentException($"A table holds 1 to {MaximumColumnCount} columns.", nameof(table));
        }

        if (table.Rows is null || table.Rows.Count > MaximumRowCount)
        {
            throw new ArgumentException($"A table holds at most {MaximumRowCount} rows.", nameof(table));
        }

        var columns = table.Columns.Select(column => column is null || !double.IsFinite(column.Weight) || column.Weight <= 0
                || !Enum.IsDefined(column.Alignment)
            ? throw new ArgumentException("A table column needs a positive, finite weight and a known alignment.", nameof(table))
            : column with { Header = Text(column.Header, nameof(column.Header)) }).ToArray();
        var rows = new List<IReadOnlyList<string>>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row is null || row.Count != columns.Length)
            {
                throw new ArgumentException("Every table row needs exactly one cell per column.", nameof(table));
            }

            rows.Add([.. row.Select(cell => Text(cell, nameof(table.Rows)))]);
        }

        return new DocumentPdfTable(columns, rows);
    }

    /// <summary>
    /// NFC text with line breaks unified to <c>\n</c>, other control characters as spaces and invisible format characters
    /// (bidirectional marks, zero-width joiners) dropped: what is drawn is what is read back.
    /// </summary>
    private static string Text(string? value, string name)
    {
        if (value is null)
        {
            throw new ArgumentException("PDF text cannot be null.", name);
        }

        if (value.Length > MaximumRawTextUnits)
        {
            throw new ArgumentException($"A PDF text exceeds the {MaximumRawTextUnits}-unit raw input limit.", name);
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(character))
            {
                case UnicodeCategory.Control:
                    builder.Append(character == '\n' ? '\n' : ' ');
                    break;
                case UnicodeCategory.Format:
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private sealed class Fonts
    {
        public XFont Title { get; } = Create(TitleFontSize);
        public XFont Heading { get; } = Create(HeadingFontSize);
        public XFont Body { get; } = Create(BodyFontSize);
        public XFont Footer { get; } = Create(FooterFontSize);
        public XFont Watermark { get; } = Create(WatermarkFontSize);

        private static XFont Create(double size) => new(
            NotoSansRegularFont.FamilyName,
            size,
            XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));
    }

    /// <summary>The cursor over the pages: it opens a page when the next piece does not fit and refuses what never fits.</summary>
    private sealed class Layout(PdfDocument document, List<PdfPage> pages, Fonts fonts) : IDisposable
    {
        private XGraphics? graphics;
        private double y;
        private double left;
        private double right;
        private double top;
        private double bottom;
        private bool pageHasContent;

        private XGraphics Graphics => graphics ?? throw new InvalidOperationException("No page is open.");

        private double Width => right - left;

        public void Title(string text)
        {
            NewPage();
            var lines = Wrap(text, fonts.Title, Width);
            var lineHeight = LineHeight(fonts.Title);
            Require(lines.Count * lineHeight);
            foreach (var line in lines)
            {
                DrawLine(line, fonts.Title, left, Width, DocumentPdfAlignment.Start);
                y += lineHeight;
            }

            pageHasContent = true;
        }

        public void Heading(string text)
        {
            Gap();
            var lines = Wrap(text, fonts.Heading, Width);
            var height = lines.Count * LineHeight(fonts.Heading);
            Ensure(height + (HeadingKeepWithLines * LineHeight(fonts.Body)), height);
            foreach (var line in lines)
            {
                DrawLine(line, fonts.Heading, left, Width, DocumentPdfAlignment.Start);
                y += LineHeight(fonts.Heading);
            }

            y += CellPaddingPoints;
            pageHasContent = true;
        }

        public void Paragraph(string text)
        {
            Gap();
            var lineHeight = LineHeight(fonts.Body);
            foreach (var line in Wrap(text, fonts.Body, Width))
            {
                Ensure(lineHeight, lineHeight);
                DrawLine(line, fonts.Body, left, Width, DocumentPdfAlignment.Start);
                y += lineHeight;
                pageHasContent = true;
            }
        }

        public void Fields(IReadOnlyList<DocumentPdfField> fields)
        {
            Gap();
            var labelWidth = Width * FieldLabelShare;
            var valueLeft = left + labelWidth;
            var valueWidth = Width - labelWidth;
            var lineHeight = LineHeight(fonts.Body);
            foreach (var field in fields)
            {
                var labels = Wrap(field.Label, fonts.Body, labelWidth - ColumnGapPoints);
                var values = Wrap(field.Value, fonts.Body, valueWidth);
                var height = Math.Max(labels.Count, values.Count) * lineHeight;
                Ensure(height, height);
                for (var index = 0; index < labels.Count; index++)
                {
                    DrawLineAt(labels[index], fonts.Body, left, labelWidth - ColumnGapPoints, DocumentPdfAlignment.Start, y + (index * lineHeight));
                }

                for (var index = 0; index < values.Count; index++)
                {
                    DrawLineAt(values[index], fonts.Body, valueLeft, valueWidth, DocumentPdfAlignment.Start, y + (index * lineHeight));
                }

                y += height;
                pageHasContent = true;
            }
        }

        public void Table(DocumentPdfTable table)
        {
            Gap();
            var totalWeight = table.Columns.Sum(column => column.Weight);
            var widths = table.Columns.Select(column => Width * column.Weight / totalWeight).ToArray();
            var header = WrapRow(table.Columns.Select(column => column.Header).ToArray(), widths);
            var rows = table.Rows.Select(row => WrapRow(row, widths)).ToArray();
            var firstRowHeight = rows.Length == 0 ? 0 : RowHeight(rows[0]);
            Ensure(RowHeight(header) + firstRowHeight, RowHeight(header) + firstRowHeight);
            DrawRow(table.Columns, widths, header, ruleWidth: RuleWidthPoints * 2);
            foreach (var row in rows)
            {
                var height = RowHeight(row);
                if (y + height > bottom)
                {
                    NewPage();
                    Require(RowHeight(header) + height);
                    DrawRow(table.Columns, widths, header, ruleWidth: RuleWidthPoints * 2);
                }

                DrawRow(table.Columns, widths, row, ruleWidth: RuleWidthPoints);
            }

            pageHasContent = true;
        }

        public void Dispose() => graphics?.Dispose();

        private List<string>[] WrapRow(IReadOnlyList<string> cells, double[] widths) =>
            [.. cells.Select((cell, index) => Wrap(cell, fonts.Body, widths[index] - (CellPaddingPoints * 2)))];

        private double RowHeight(List<string>[] cells) =>
            (cells.Max(lines => lines.Count) * LineHeight(fonts.Body)) + (CellPaddingPoints * 2);

        private void DrawRow(IReadOnlyList<DocumentPdfColumn> columns, double[] widths, List<string>[] cells, double ruleWidth)
        {
            var lineHeight = LineHeight(fonts.Body);
            var x = left;
            for (var column = 0; column < cells.Length; column++)
            {
                var lines = cells[column];
                for (var index = 0; index < lines.Count; index++)
                {
                    DrawLineAt(lines[index], fonts.Body, x + CellPaddingPoints, widths[column] - (CellPaddingPoints * 2),
                        columns[column].Alignment, y + CellPaddingPoints + (index * lineHeight));
                }

                x += widths[column];
            }

            y += RowHeight(cells);
            Graphics.DrawLine(new XPen(XColors.Gray, ruleWidth), left, y, right, y);
            pageHasContent = true;
        }

        private void Gap()
        {
            if (pageHasContent)
            {
                y += BlockGapPoints;
            }
        }

        /// <summary>Opens a new page when <paramref name="wanted"/> does not fit here; <paramref name="needed"/> must fit on a page.</summary>
        private void Ensure(double wanted, double needed)
        {
            if (y + wanted > bottom && pageHasContent)
            {
                NewPage();
            }

            Require(needed);
        }

        private void Require(double height)
        {
            if (y + height > bottom)
            {
                throw new ArgumentException("A block of the document is taller than one page and cannot be laid out without cutting it.");
            }
        }

        private void NewPage()
        {
            if (pages.Count >= MaximumPageCount)
            {
                throw new ArgumentException($"The document exceeds the {MaximumPageCount}-page limit.");
            }

            graphics?.Dispose();
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(PageWidthMillimeters);
            page.Height = XUnit.FromMillimeter(PageHeightMillimeters);
            pages.Add(page);
            graphics = XGraphics.FromPdfPage(page);
            left = MarginPoints;
            right = page.Width.Point - MarginPoints;
            top = MarginPoints;
            bottom = page.Height.Point - MarginPoints - FooterReservePoints;
            y = top;
            pageHasContent = false;
        }

        private void DrawLine(string line, XFont font, double x, double width, DocumentPdfAlignment alignment) =>
            DrawLineAt(line, font, x, width, alignment, y);

        private void DrawLineAt(string line, XFont font, double x, double width, DocumentPdfAlignment alignment, double lineTop)
        {
            if (line.Length == 0)
            {
                return;
            }

            var start = alignment == DocumentPdfAlignment.End ? x + width - Graphics.MeasureString(line, font).Width : x;
            Graphics.DrawString(line, font, XBrushes.Black, new XPoint(start, lineTop + font.Size));
        }

        /// <summary>Greedy word wrap inside <paramref name="width"/>; a word wider than the width breaks between text elements.</summary>
        private List<string> Wrap(string text, XFont font, double width)
        {
            if (width <= 0)
            {
                throw new ArgumentException("A column is too narrow to hold any text.");
            }

            var lines = new List<string>();
            foreach (var hardLine in text.Split('\n'))
            {
                var current = new StringBuilder();
                foreach (var word in hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = current.Length == 0 ? word : string.Concat(current.ToString(), " ", word);
                    if (Measure(candidate, font) <= width)
                    {
                        current.Clear().Append(candidate);
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }

                    if (Measure(word, font) <= width)
                    {
                        current.Append(word);
                        continue;
                    }

                    var elements = StringInfo.GetTextElementEnumerator(word);
                    while (elements.MoveNext())
                    {
                        var element = elements.GetTextElement();
                        if (current.Length > 0 && Measure(string.Concat(current.ToString(), element), font) > width)
                        {
                            lines.Add(current.ToString());
                            current.Clear();
                        }

                        current.Append(element);
                    }
                }

                lines.Add(current.ToString());
            }

            return lines;
        }

        private double Measure(string text, XFont font) => Graphics.MeasureString(text, font).Width;

        private static double LineHeight(XFont font) => font.Size * LineHeightFactor;
    }
}
