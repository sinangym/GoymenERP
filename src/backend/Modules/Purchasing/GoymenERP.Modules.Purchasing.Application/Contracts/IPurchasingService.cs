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

    /// <summary>A1-1 (ALS-D2): Submitted → Approved under <c>purchasing.requests.approve</c>; the approver is neither preparer nor submitter.</summary>
    Task<PurchaseRequestResponse> ApproveRequestAsync(Guid requestId, ApprovePurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>A1-1 (No.161 §1): Submitted | Approved → Rejected with a reason; refused while an APPROVED order is bound.</summary>
    Task<PurchaseRequestResponse> RejectRequestAsync(Guid requestId, RejectPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>A1-1b (ALS-D46, ALS-D47): Submitted → Withdrawn with a reason under <c>purchasing.requests.withdraw</c>, by the recorded preparer or submitter only.</summary>
    Task<PurchaseRequestResponse> WithdrawRequestAsync(Guid requestId, WithdrawPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<PurchaseRequestListResponse> GetRequestListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken = default);

    Task<PurchaseOrderResponse> CreateOrderAsync(CreatePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrderResponse> ApproveOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A1-3 (plan API-10; No.161 §2): Approved → Cancelled under <c>purchasing.orders.cancel</c> — the whole unaccepted remainder of
    /// every line, with a reason; accepted shares, lines and the request binding stay; nothing physical or financial is recorded.
    /// </summary>
    Task<PurchaseOrderResponse> CancelOrderAsync(Guid orderId, CancelPurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>A1-3 (plan API-10): the cancellation's impact preview under <c>purchasing.orders.cancel</c>; reads only.</summary>
    Task<PurchaseOrderCancelImpactView> GetOrderCancelImpactAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>A2a-2 (API-20): the order's totals under the read name; amounts only with the price read (PRS-02).</summary>
    Task<PurchaseOrderTotalsView> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<PurchaseOrderListResponse> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default);

    /// <summary>A2a-1 (TEK-01): the currencies the purchasing order operation's Settings money profile answers, under the create name.</summary>
    Task<PurchasingCurrencyCandidatesResponseV1> GetCurrencyCandidatesAsync(CancellationToken cancellationToken = default);
}
