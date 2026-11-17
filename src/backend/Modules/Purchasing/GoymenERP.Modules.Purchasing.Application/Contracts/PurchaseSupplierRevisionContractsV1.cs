using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record RevisePurchaseOrderSupplierRequest(string ExpectedVersion, Guid SupplierPartyId, Guid CompanyAccountId, string Reason);
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
    public static RevisePurchaseOrderSupplierRequest ReadRevision(string json) =>
        JsonSerializer.Deserialize<RevisePurchaseOrderSupplierRequest>(json, PurchasingJson.Options) ?? throw new JsonException("Request body required.");
    public static SetPurchaseSupplierRevisionPriceRequest ReadPrice(string json) =>
        JsonSerializer.Deserialize<SetPurchaseSupplierRevisionPriceRequest>(json, PurchasingJson.Options) ?? throw new JsonException("Request body required.");
}
