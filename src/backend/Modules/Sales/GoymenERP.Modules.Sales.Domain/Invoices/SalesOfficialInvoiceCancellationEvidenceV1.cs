namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>The external owner's pinned status record. A trusted provider supplies it; the public command cannot.
/// It preserves the actual official cancellation time separately from the ERP recording time.</summary>
public sealed record SalesOfficialInvoiceCancellationEvidenceV1(Guid CompanyId, Guid InvoiceId,
    string OfficialInvoiceNo, string Ettn, string SourceKind, string SourceRecordId, string SourceVersion,
    string SourcePayloadSha256, DateTimeOffset OfficiallyCancelledAtUtc)
{
    public bool IsValidFor(Guid companyId, Guid invoiceId, string officialInvoiceNo, string ettn) =>
        companyId != Guid.Empty && CompanyId == companyId && invoiceId != Guid.Empty && InvoiceId == invoiceId
        && OfficialInvoiceNo == officialInvoiceNo && Text(officialInvoiceNo, SalesInvoice.MaximumOfficialNoLength)
        && Ettn == ettn && Guid.TryParseExact(ettn, "D", out var identity) && identity != Guid.Empty
        && Text(SourceKind, 128) && Text(SourceRecordId, 256) && Text(SourceVersion, 128)
        && SourcePayloadSha256 is { Length: 64 } && SourcePayloadSha256.All(char.IsAsciiHexDigit)
        && OfficiallyCancelledAtUtc != default && OfficiallyCancelledAtUtc.Offset == TimeSpan.Zero;

    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text == text.Trim()
        && text.Length <= maximum && !text.Any(char.IsControl);
}
