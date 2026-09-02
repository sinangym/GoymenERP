using GoymenERP.DocumentGeneration.Pdf;

// Development-only PoC entry point. It emits fixed synthetic data and has no network/runtime fetch.
var outputDirectory = args.Length switch
{
    0 => Path.Combine(AppContext.BaseDirectory, "poc-output"),
    1 => Path.GetFullPath(args[0]),
    _ => throw new ArgumentException("Usage: GoymenERP.PdfPoc [output-directory]"),
};

Directory.CreateDirectory(outputDirectory);

var artifact = new TechnicalPdfGenerator().Generate(new PdfGenerationRequest(
    Guid.Parse("00000000-0000-0000-0000-000000000066"),
    new PdfDocumentFileNameParts(
        "Satış Sipariş Sözleşmesi",
        "SPS-2026-01842",
        "ABC Demir",
        new DateOnly(2026, 8, 25),
        1,
        "Onaylı"),
    [
        "Teknik PDF PoC",
        "İ ı Ş ş Ğ ğ Ü ü Ö ö Ç ç — Satış sipariş sözleşmesi onaylıdır.",
    ]));

var outputPath = Path.Combine(outputDirectory, artifact.VisibleFileName);
File.WriteAllBytes(outputPath, artifact.Content);

Console.WriteLine(outputPath);
Console.WriteLine($"{artifact.MediaType} | {artifact.Content.Length} bytes | {artifact.TechnicalId}");
