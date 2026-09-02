namespace GoymenERP.DocumentGeneration.Pdf;

public sealed record PdfDocumentFileNameParts(
    string DocumentType,
    string DocumentNumber,
    string CounterpartyShortName,
    DateOnly DocumentDate,
    int Version,
    string Status);
