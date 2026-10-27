namespace GoymenERP.DocumentGeneration.Pdf;

/// <summary>
/// A business document laid out as text on A4 landscape pages: a title, then the blocks in order, and on every page the
/// footer text and the page label. Every string is the caller's final, already localized text; the generator measures,
/// wraps and pages it but never formats, computes or invents a value. The visible file name follows DEC-DOC-004 and is
/// independent of <see cref="TechnicalId"/>.
/// </summary>
/// <param name="PageLabelFormat">A composite format with <c>{0}</c> the page number and <c>{1}</c> the page count.</param>
/// <param name="Watermark">
/// Optional large diagonal text drawn behind the content of EVERY page (e.g. a draft marker); null draws none. It stays
/// extractable text, so the page text carries it too.
/// </param>
public sealed record DocumentPdfRequest(
    Guid TechnicalId,
    PdfDocumentFileNameParts FileNameParts,
    string Title,
    IReadOnlyList<DocumentPdfBlock> Blocks,
    string FooterText,
    string PageLabelFormat,
    string? Watermark = null);

/// <summary>One block of a <see cref="DocumentPdfRequest"/>; the closed set below is the whole layout vocabulary.</summary>
public abstract record DocumentPdfBlock;

/// <summary>A section heading; it is never left alone at the bottom of a page.</summary>
public sealed record DocumentPdfHeading(string Text) : DocumentPdfBlock;

/// <summary>Running text, wrapped to the content width; a line break in the text starts a new line.</summary>
public sealed record DocumentPdfParagraph(string Text) : DocumentPdfBlock;

/// <summary>Label and value pairs, the labels in a narrow column beside their wrapped values.</summary>
public sealed record DocumentPdfFields(IReadOnlyList<DocumentPdfField> Fields) : DocumentPdfBlock;

public sealed record DocumentPdfField(string Label, string Value);

/// <summary>
/// A table whose header row repeats on every page it continues on. Column widths are the weights' share of the content
/// width; each cell wraps inside its column. Every row has exactly one cell per column.
/// </summary>
public sealed record DocumentPdfTable(IReadOnlyList<DocumentPdfColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
    : DocumentPdfBlock;

public sealed record DocumentPdfColumn(string Header, double Weight, DocumentPdfAlignment Alignment = DocumentPdfAlignment.Start);

public enum DocumentPdfAlignment
{
    Start = 0,
    End = 1,
}
