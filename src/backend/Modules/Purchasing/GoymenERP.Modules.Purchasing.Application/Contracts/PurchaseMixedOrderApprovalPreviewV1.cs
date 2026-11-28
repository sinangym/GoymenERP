using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseMixedOrderApprovalSelectionV1(Guid ServiceOrderId, string ExpectedVersion, IReadOnlyList<Guid> LineIds);
public sealed record PurchaseMixedOrderApprovalPreviewV1(Guid CompanyId, PurchaseOrderDetailView Parent,
    IReadOnlyList<PurchaseMixedOrderServicePreviewV1> Services);
public sealed record PurchaseMixedOrderServicePreviewV1(PurchaseServiceOrderViewV1 Service, string InitialPreparedVersion,
    IReadOnlyList<Guid> InitialLineIds, string PreparedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    int PriceRevision, string PartyVersion, string AccountVersion, PurchaseMixedOrderApprovalSelectionV1 Selection);

// Private prepared facts. A preview is neither an approval nor a persisted command/receipt.
public sealed record PurchaseMixedOrderPreparedPreviewV1(
    [property: JsonIgnore] StoredPurchaseOrder Parent,
    [property: JsonIgnore] IReadOnlyList<PurchaseServiceOrderState> Services);

public interface IPurchaseMixedOrderApprovalPreviewV1
{
    Task<PurchaseMixedOrderApprovalPreviewV1> ReadMixedOrderApprovalPreviewAsync(Guid orderId, string expectedVersion,
        CancellationToken cancellationToken = default);
}
public interface IPurchaseMixedOrderApprovalPreviewStoreV1
{
    Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedOrderApprovalPreviewAsync(Guid orderId, long expectedVersion,
        Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderPreparedPreviewV1>> prepare,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseMixedOrderPreparedPreviewV1?>(new PurchasingServiceException(503, "service_unavailable"));
}

/// <summary>Exact private owner equality; only native supplier-version refresh is optional.</summary>
public static class PurchaseMixedOrderApprovalFactsV1
{
    public static bool SameChild(PurchaseServiceOrderState a, PurchaseServiceOrderState b, bool allowSupplierRefresh = false) =>
        (a with { Lines = b.Lines, Materials = b.Materials, ExpectedOutputs = b.ExpectedOutputs, Acceptances = b.Acceptances,
            PartyVersion = allowSupplierRefresh ? b.PartyVersion : a.PartyVersion,
            AccountVersion = allowSupplierRefresh ? b.AccountVersion : a.AccountVersion }) == b
        && a.Lines.SequenceEqual(b.Lines) && a.Materials.SequenceEqual(b.Materials)
        && a.ExpectedOutputs.SequenceEqual(b.ExpectedOutputs) && a.Acceptances.SequenceEqual(b.Acceptances);

}
