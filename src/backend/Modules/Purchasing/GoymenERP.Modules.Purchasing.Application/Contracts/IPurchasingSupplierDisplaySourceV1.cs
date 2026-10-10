namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Purchasing-owned consumer port for the Cari owner's current canonical supplier presentation.
/// Party ids must come only from already-authorized purchase orders; this is not a general Cari lookup.
/// </summary>
public interface IPurchasingSupplierDisplaySourceV1
{
    Task<PurchasingSupplierDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        PurchasingSupplierDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default);
}

public sealed record PurchasingSupplierDisplayBatchRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public enum PurchasingSupplierDisplayBatchOutcomeV1
{
    Success,
    Denied,
    Unavailable,
}

public sealed record PurchasingSupplierDisplayBatchAnswerV1(
    PurchasingSupplierDisplayBatchOutcomeV1 Outcome,
    IReadOnlyList<PurchasingSupplierDisplayV1> Items);

public sealed record PurchasingSupplierDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
