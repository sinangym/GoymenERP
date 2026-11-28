using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>The authorized person's actual occurrence declaration. BusinessDate is explicit, never inferred from
/// a file date or UTC timestamp. Reason is optional and retained verbatim; this is not a departmental approval.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordPurchaseUnorderedAcceptanceV2([property: JsonRequired] RecordPurchaseUnorderedAcceptanceV1 Occurrence,
    [property: JsonRequired] DateOnly BusinessDate, string? Reason = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseUnorderedAcceptanceDeclarationV2([property: JsonRequired] DateOnly BusinessDate, string? Reason);

/// <summary>Only a clean, readable immutable Documents version. Found does not certify physical receipt,
/// quantity, a semantic source line, business date, commercial control or approval.</summary>
public sealed record PurchaseUnorderedAcceptanceDocumentFactsV1(Guid CompanyId, Guid DocumentId, long DocumentVersion, string Sha256);
public sealed record PurchaseUnorderedAcceptanceDocumentAnswerV1(PurchaseUnorderedAcceptanceEvidenceOutcomeV1 Outcome,
    PurchaseUnorderedAcceptanceDocumentFactsV1? Facts);

public interface IPurchaseUnorderedAcceptanceDocumentReadV1
{
    Task<PurchaseUnorderedAcceptanceDocumentAnswerV1> ReadAsync(Guid companyId, PurchaseAcceptanceDocumentEvidenceV1 evidence,
        Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken);
}
