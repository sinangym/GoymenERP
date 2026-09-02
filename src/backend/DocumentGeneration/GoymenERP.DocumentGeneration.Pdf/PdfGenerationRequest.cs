namespace GoymenERP.DocumentGeneration.Pdf;

public sealed record PdfGenerationRequest(
    Guid TechnicalId,
    PdfDocumentFileNameParts FileNameParts,
    IReadOnlyList<string> Lines);
