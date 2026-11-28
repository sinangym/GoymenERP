namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Read-only Approved price-revision purpose. It is not the Draft approval preview or a new approval.</summary>
public sealed record PurchaseMixedServicePriceRevisionPreviewV1(Guid CompanyId, PurchaseOrderDetailView Parent,
    IReadOnlyList<PurchaseMixedOrderServicePreviewV1> Services);
public interface IPurchaseMixedServicePriceRevisionPreviewV1
{
    Task<PurchaseMixedServicePriceRevisionPreviewV1> ReadMixedServicePriceRevisionPreviewAsync(Guid orderId, string expectedVersion,
        CancellationToken cancellationToken = default);
}
public interface IPurchaseMixedServicePriceRevisionPreviewStoreV1
{
    Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedServicePriceRevisionPreviewAsync(Guid orderId, long expectedVersion,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseMixedOrderPreparedPreviewV1?>(new PurchasingServiceException(503, "service_unavailable"));
}
