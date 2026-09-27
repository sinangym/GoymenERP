namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Immutable Purchasing acceptance facts for a normal goods-receipt reopen. Not a replay/write and no
/// idempotency key, actor or hash is disclosed. The consumer authorizes the Purchasing read and fences
/// company/actor before and after the awaited call. Missing/foreign references are indistinguishable null.
/// </summary>
public interface IPurchaseAcceptanceSourceV1
{
    Task<PurchaseAcceptanceSourceV1?> ReadAsync(Guid purchaseOrderId, Guid acceptanceId,
        CancellationToken cancellationToken = default);
}

public sealed record PurchaseAcceptanceSourceV1(Guid CompanyId, Guid AcceptanceId, Guid PurchaseOrderId,
    Guid PurchaseOrderLineId, string PurchaseOrderVersionAfter, string AcceptedQuantity, string RemainingQuantity,
    DateTimeOffset OccurredAtUtc);
