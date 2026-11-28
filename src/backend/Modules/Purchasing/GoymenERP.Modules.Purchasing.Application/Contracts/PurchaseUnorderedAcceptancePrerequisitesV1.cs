namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Actual retained document-line facts, not an echo of a requested line. The source owner must bind
/// every supplied document/version/hash/line to this company, supplier/account, subject, unit and actual quantity.
/// BusinessDate is the owner's evidenced effective date; absence is unavailable, never UTC-date/timezone inference.
/// Control evidence may be empty or pending and never certifies a commercial/accounting/payment approval.</summary>
public sealed record PurchaseUnorderedAcceptanceEvidenceFactsV1(Guid CompanyId, Guid AcceptanceId, Guid AcceptanceLineId,
    Guid SupplierPartyId, Guid CompanyAccountId, PurchaseUnorderedAcceptanceSubjectV1 Subject, Guid UnitId,
    string AcceptedQuantity, DateTimeOffset OccurredAtUtc, DateOnly? BusinessDate,
    PurchaseAcceptanceDocumentEvidenceV1 Evidence, IReadOnlyList<PurchaseAcceptanceDocumentEvidenceV1> ControlEvidence);

public enum PurchaseUnorderedAcceptanceEvidenceOutcomeV1 { Unavailable = 0, Denied = 1, NotFound = 2, Found = 3 }
public sealed record PurchaseUnorderedAcceptanceEvidenceAnswerV1(PurchaseUnorderedAcceptanceEvidenceOutcomeV1 Outcome,
    PurchaseUnorderedAcceptanceEvidenceFactsV1? Facts);

/// <summary>Legacy v1 semantic source-owner bridge; its command and original receipts retain their exact behavior.
/// New explicit occurrence declarations use IPurchaseUnorderedAcceptanceDocumentReadV1 instead of requiring their own prior existence.
/// Verify clean, readable immutable versions and real
/// line/business-date provenance under current owner permissions, fencing the caller after awaited owner reads.
/// An invoice-import reservation or a PO must not be fabricated from AcceptanceId. No permissive implementation
/// is registered: until actual source evidence is connected the composite refuses the occurrence with 503.
/// This read grants nothing, posts no stock/Finance, and supplies no prices or unit conversions.</summary>
public interface IPurchaseUnorderedAcceptanceEvidenceReadV1
{
    Task<PurchaseUnorderedAcceptanceEvidenceAnswerV1> ReadAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken);
}
