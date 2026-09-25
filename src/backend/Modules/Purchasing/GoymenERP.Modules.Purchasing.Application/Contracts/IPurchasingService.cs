namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The purchasing use cases of PUR-4a: a purchase request (Draft with lines → Submitted) and a purchase
/// order (Draft with lines → Approved), list and detail of each. Implementations obtain company, actor and
/// session from the authenticated server scope and re-measure the Purchasing permission at every fence
/// (#276), before the replay is read (ADR-011 §5) and right before the single save — never after the
/// commit (S03). Nothing here is physical: the goods receipt, its kantar quantity and the stock-in are
/// PUR-4b + Inventory T09; price is T28; the approval is a state transition, which approvals a purchase
/// needs is §8 policy and not chosen here.
/// </summary>
public interface IPurchasingService
{
    Task<PurchaseRequestResponse> CreateRequestAsync(CreatePurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseRequestResponse> SubmitRequestAsync(Guid requestId, SubmitPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<PurchaseRequestListResponse> GetRequestListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseOrderResponse> CreateOrderAsync(CreatePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrderResponse> ApproveOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<PurchaseOrderListResponse> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default);
}
