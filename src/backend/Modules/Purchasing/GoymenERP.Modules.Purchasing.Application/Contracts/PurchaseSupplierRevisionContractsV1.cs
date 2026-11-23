using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// ALS-D112/D115 supplier revision. ALS-D126: <c>seenAllocations</c> (optional) is the set of active future allocations on the
/// order's general lines the actor saw and confirmed (ids and owner versions, as the GET …/future-allocations list shows them).
/// Without it an active allocation refuses the revision (409 <c>purchasing_line_has_allocations</c>, not stored); with it the set
/// must be exactly the active set (409 <c>purchasing_future_allocation_set_changed</c>) and the revision releases them through the
/// Inventory owner in the same transaction, audited. An empty set confirms that none is active.
/// </summary>
public sealed record RevisePurchaseOrderSupplierRequest(string ExpectedVersion, Guid SupplierPartyId, Guid CompanyAccountId, string Reason,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PurchaseFutureAllocationSeenRequest>? SeenAllocations = null);
public sealed record SetPurchaseSupplierRevisionPriceRequest(string ExpectedVersion, PurchaseOrderLinePriceRequest Price);
public sealed record PurchaseSupplierRevisionView(int Revision, string OrderVersionAfter, Guid PreviousSupplierPartyId, Guid SupplierPartyId,
    string PreparedBy, string PreviousApprovedBy, DateTimeOffset PreviousApprovedAtUtc, string Reason, DateTimeOffset RecordedAtUtc);
public sealed record PurchaseSupplierRevisionHistoryView(Guid CompanyId, Guid OrderId, string Version, int Revision,
    IReadOnlyList<PurchaseSupplierRevisionView> Revisions);

public interface IPurchasingSupplierRevisionServiceV1
{
    Task<PurchaseOrderResponse> ReviseOrderSupplierAsync(Guid orderId, RevisePurchaseOrderSupplierRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseOrderResponse> SetSupplierRevisionPriceAsync(Guid orderId, Guid lineId, SetPurchaseSupplierRevisionPriceRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierRevisionHistoryView> GetSupplierRevisionsAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public static class PurchaseSupplierRevisionJsonV1
{
    public static RevisePurchaseOrderSupplierRequest ReadRevision(string json)
    {
        var request = JsonSerializer.Deserialize<RevisePurchaseOrderSupplierRequest>(json, PurchasingJson.Options)
            ?? throw new JsonException("Request body required.");
        if (request.SeenAllocations is not null) PurchasingJson.RequireSeenShape(request.SeenAllocations);
        return request;
    }
    public static SetPurchaseSupplierRevisionPriceRequest ReadPrice(string json) =>
        JsonSerializer.Deserialize<SetPurchaseSupplierRevisionPriceRequest>(json, PurchasingJson.Options) ?? throw new JsonException("Request body required.");
}
