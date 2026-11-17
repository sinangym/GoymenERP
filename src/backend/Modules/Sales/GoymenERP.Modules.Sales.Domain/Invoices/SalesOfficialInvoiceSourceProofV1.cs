namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>The approved Finance owner's immutable source identity, retained with the match; never inferred from the draft.</summary>
public sealed record SalesOfficialInvoiceSourceProofV1(
    Guid CompanyId,
    Guid InvoiceId,
    string SourceKind,
    string Version,
    DateTimeOffset ApprovedAtUtc,
    Guid? PdfDocumentId,
    int? PdfDocumentVersionNo,
    string? PdfSha256,
    Guid? SourceBatchId,
    string? SourceChecksum,
    bool HasPendingManual)
{
    public bool IsValidFor(Guid companyId) => companyId != Guid.Empty && CompanyId == companyId && InvoiceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(Version) && Version.Length <= 128 && Version == Version.Trim()
        && ApprovedAtUtc != default && ApprovedAtUtc.Offset == TimeSpan.Zero
        && (SourceKind switch
        {
            "manual" => PdfDocumentId is { } pdf && pdf != Guid.Empty && PdfDocumentVersionNo > 0 && IsSha256(PdfSha256)
                && SourceBatchId is null && SourceChecksum is null && !HasPendingManual,
            "approved-import" => SourceBatchId is { } batch && batch != Guid.Empty && IsSha256(SourceChecksum)
                && PdfDocumentId is null && PdfDocumentVersionNo is null && PdfSha256 is null,
            _ => false,
        });

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
