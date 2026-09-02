namespace GoymenERP.DocumentGeneration.Pdf;

public sealed record GeneratedPdf(
    Guid TechnicalId,
    string VisibleFileName,
    string MediaType,
    byte[] Content);
