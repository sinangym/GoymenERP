using System.Data;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseServiceSupplierRevisionFactsV1([property: JsonIgnore] PurchaseServiceOrderState Original,
    PurchaseOrderInvoiceHistory InvoiceHistory);
public sealed record PurchaseServiceSupplierRevisionPreparationV1(Guid CompanyId, string ActorId,
    [property: JsonIgnore] PurchaseServiceOrderState Original, [property: JsonIgnore] PurchaseServiceOrderState Candidate);
public sealed record PurchaseMixedSupplierRevisionChildV1(PurchaseServiceOrderState Original, PurchaseServiceOrderState Revised);
public sealed record PurchaseMixedSupplierRevisionReceiptV1(Guid CompanyId, Guid OrderId, string ActorId,
    string KeyHash, string PayloadHash, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc,
    long ExpectedVersion, long ResultVersion, Guid PreviousCompanyAccountId, Guid CompanyAccountId,
    RevisePurchaseOrderSupplierRequest FrozenRequest, PurchaseSupplierRevision NativeRevision,
    IReadOnlyList<PurchaseMixedSupplierRevisionChildV1> Children, PurchaseOrderDetailView Visible, PurchaseOrderDetailView Withheld);
public sealed record PurchaseMixedSupplierRevisionAnswerV1([property: JsonIgnore] PurchaseMixedSupplierRevisionReceiptV1? Receipt, bool IsReplay, PurchasingTerminalRefusal? Refusal = null, string? OriginalCorrelationId = null);
public interface IPurchaseMixedSupplierRevisionStoreV1
{
    bool MixedSupplierRevisionEntryIsClean => false;
    Task<PurchaseMixedSupplierRevisionAnswerV1> ReviseMixedSupplierAsync(Guid id, RevisePurchaseOrderSupplierRequest request,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<IReadOnlyList<PurchaseServiceSupplierRevisionPreparationV1>>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> allocationGuard, CancellationToken token = default) =>
        Task.FromException<PurchaseMixedSupplierRevisionAnswerV1>(new PurchasingServiceException(503, "service_unavailable"));
}
public static class PurchaseMixedSupplierRevisionIntentV1
{
    public static RevisePurchaseOrderSupplierRequest Freeze(RevisePurchaseOrderSupplierRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = PurchaseMixedOrderApprovalIntentV1.Freeze(new(request.ExpectedVersion) { MixedServices = request.MixedServices });
        if (request.SupplierPartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > PurchaseOrder.SupplierRevisionReasonMaximumLength)
            throw new PurchasingServiceException(422, "validation_failed");
        return request with { MixedServices = selected.MixedServices,
            SeenAllocations = request.SeenAllocations is null ? null : Array.AsReadOnly(request.SeenAllocations.ToArray()) };
    }
}
