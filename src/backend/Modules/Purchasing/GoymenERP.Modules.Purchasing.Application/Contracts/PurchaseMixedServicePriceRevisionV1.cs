using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseMixedServicePriceChangeV1(Guid ServiceOrderId, IReadOnlyList<PurchaseServiceLinePriceV1> Lines);
public sealed record RevisePurchaseMixedServicePricesRequest(string ExpectedVersion,
    IReadOnlyList<PurchaseMixedOrderApprovalSelectionV1> Services, IReadOnlyList<PurchaseMixedServicePriceChangeV1> Changes, string Reason);
public interface IPurchaseMixedServicePriceRevisionServiceV1
{
    Task<PurchaseOrderResponse> ReviseMixedServicePricesAsync(Guid orderId, RevisePurchaseMixedServicePricesRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}
public sealed record PurchaseMixedServicePriceRecordedChildV1(PurchaseServiceOrderState Before, PurchaseServiceOrderState After);
public sealed record PurchaseMixedServicePriceReceiptV1(Guid CompanyId, Guid OrderId, string ActorId, string KeyHash,
    string PayloadHash, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, long ExpectedVersion, long ResultVersion,
    RevisePurchaseMixedServicePricesRequest FrozenRequest, PurchaseMixedServicePriceRevision NativeRevision,
    IReadOnlyList<PurchaseMixedServicePriceRecordedChildV1> Children, PurchaseOrderDetailView Visible, PurchaseOrderDetailView Withheld);
public sealed record PurchaseMixedServicePriceAnswerV1([property: JsonIgnore] PurchaseMixedServicePriceReceiptV1? Receipt, bool IsReplay,
    PurchasingTerminalRefusal? Refusal = null, string? OriginalCorrelationId = null);
public interface IPurchaseMixedServicePriceRevisionStoreV1
{
    bool MixedServicePriceEntryIsClean => false;
    Task<PurchaseMixedServicePriceAnswerV1> ReviseMixedServicePricesAsync(Guid orderId, RevisePurchaseMixedServicePricesRequest request,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default) => Task.FromException<PurchaseMixedServicePriceAnswerV1>(
            new PurchasingServiceException(503, "service_unavailable"));
}

public static class PurchaseMixedServicePriceIntentV1
{
    public const string Command = PurchaseOrder.ServicePriceRevisionKind;
    public const string Event = "purchasing.purchase-order.service-prices-revised.v1";
    public static RevisePurchaseMixedServicePricesRequest Freeze(RevisePurchaseMixedServicePricesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Reason is null) throw new PurchasingServiceException(400, "validation_failed");
        var selection = PurchaseMixedOrderMembershipV1.Freeze(new(request.ExpectedVersion, request.Services));
        if (request.Changes is not { Count: > 0 } changes || changes.Any(x => x is null || x.ServiceOrderId == Guid.Empty
                || x.Lines is not { Count: > 0 } || x.Lines.Any(line => line is null || line.LineId == Guid.Empty || line.UnitPrice is null)
                || x.Lines.Select(line => line.LineId).Distinct().Count() != x.Lines.Count)
            || changes.Select(x => x.ServiceOrderId).Distinct().Count() != changes.Count
            || changes.Any(x => !selection.Services.Any(s => s.ServiceOrderId == x.ServiceOrderId)))
            throw new PurchasingServiceException(400, "validation_failed");
        foreach (var changed in changes)
            _ = PurchaseMixedOrderApprovalIntentV1.Version(selection.Services.Single(x => x.ServiceOrderId == changed.ServiceOrderId).ExpectedVersion);
        return request with { Services = selection.Services, Changes = Array.AsReadOnly(changes.Select(x => x with {
            Lines = Array.AsReadOnly(x.Lines.Select(line => line with { }).ToArray()) }).ToArray()) };
    }
}
