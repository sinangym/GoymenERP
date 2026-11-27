using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D130: an actual, company-owned document line, not a fabricated purchase order.
/// The acceptance owner must verify the retained Documents identity/version/hash before answering Found.</summary>
public sealed record PurchaseAcceptanceDocumentEvidenceV1(Guid DocumentId, long DocumentVersion,
    string Sha256, string SourceLineId);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PurchaseUnorderedGoodsSubjectV1), "Goods")]
[JsonDerivedType(typeof(PurchaseUnorderedServiceSubjectV1), "Service")]
public abstract record PurchaseUnorderedAcceptanceSubjectV1;
public sealed record PurchaseUnorderedGoodsSubjectV1(Guid ProductId, string ProductVersion)
    : PurchaseUnorderedAcceptanceSubjectV1;
public sealed record PurchaseUnorderedServiceSubjectV1(Guid ServiceTypeId, string ServiceTypeVersion)
    : PurchaseUnorderedAcceptanceSubjectV1;

/// <summary>Owner facts only. UninvoicedQuantity is the currently unbound share of THIS acceptance line,
/// in UnitId, not stock availability. No kg conversion or money benchmark is inferred.
/// ControlEvidence contains only actual retained evidence; empty means no recorded control evidence,
/// never approval. Its count does not prescribe approval stages or authorize matching/finalisation/payment.</summary>
public sealed record PurchaseUnorderedAcceptanceSourceV1(Guid CompanyId, Guid AcceptanceId, Guid AcceptanceLineId,
    long Version, Guid SupplierPartyId, Guid CompanyAccountId, PurchaseUnorderedAcceptanceSubjectV1 Subject,
    Guid UnitId, string AcceptedQuantity, string UninvoicedQuantity, DateTimeOffset OccurredAtUtc,
    PurchaseAcceptanceDocumentEvidenceV1 Evidence, IReadOnlyList<PurchaseAcceptanceDocumentEvidenceV1> ControlEvidence);

public enum PurchaseUnorderedAcceptanceSourceOutcomeV1 { Found = 1, NotFound = 2, Unavailable = 3 }
public sealed record PurchaseUnorderedAcceptanceSourceAnswerV1(PurchaseUnorderedAcceptanceSourceOutcomeV1 Outcome,
    PurchaseUnorderedAcceptanceSourceV1? Source);

/// <summary>Trusted server company, exact acceptance/line. Missing and foreign sources are indistinguishable
/// NotFound. An absent persistent owner is Unavailable.
/// Called under the consumer's current company/actor/permission fence. A future matching consumer must
/// additionally serialize the actual acceptance share and retain its evidence; this read is not a reservation.</summary>
public interface IPurchaseUnorderedAcceptanceSourceV1
{
    Task<PurchaseUnorderedAcceptanceSourceAnswerV1> ReadAsync(Guid companyId, Guid acceptanceId,
        Guid acceptanceLineId, CancellationToken cancellationToken = default);
}
