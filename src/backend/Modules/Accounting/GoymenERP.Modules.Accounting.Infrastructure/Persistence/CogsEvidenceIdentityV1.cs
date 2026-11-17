using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// Accounting's price-free identity for a frozen invoice-line cost claim. A cancellation reads its already
/// accepted source; it must not invent a current Sales invoice or reuse the commercial edit admission.
/// Public SAT-D422 commands still validate their complete FinalizedInvoiceLineSourceV1 before conversion.
/// </summary>
internal sealed record CogsEvidenceIdentityV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId,
    Guid FinalizationAdmissionId, long FinalizedVersion, Guid ProductId, Guid ShipmentId,
    Guid DispatchNoteId, string Fingerprint, bool CancellationRead = false)
{
    public static implicit operator CogsEvidenceIdentityV1(FinalizedInvoiceLineSourceV1 source) => new(
        source.CompanyId, source.InvoiceId, source.InvoiceLineId, source.FinalizationAdmissionId,
        source.FinalizedVersion, source.ProductId, source.ShipmentId, source.DispatchNoteId, source.Fingerprint);
}
