using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryGoodsReceiptReversalKindV1 { AcceptanceUndo = 1, SupplierReturn = 2 }

/// <summary>STK-S19 / F017-e: Inventory owns original conversion, physical reversal and reopening; the host owns its transaction.</summary>
public interface IInventoryGoodsReceiptReversalParticipantV1 : IAsyncDisposable
{
    Task<InventoryGoodsReceiptReversalTargetV1?> ReadTargetAsync(Guid acceptanceId,
        CancellationToken cancellationToken = default);
    Task<InventoryGoodsReceiptReversalResultV1?> ReadReplayAsync(InventoryGoodsReceiptReversalRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<InventoryGoodsReceiptReversalResultV1> StageAsync(InventoryGoodsReceiptReversalRequestV1 request,
        InventoryGoodsReceiptReversalSupplyV1 expectedSupply, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>All identities come from Inventory's original receipt and conversion rows; no authority is granted by this read.</summary>
public sealed record InventoryGoodsReceiptReversalTargetV1(InventoryGoodsReceiptSourceV1 Original,
    string LabelVersion, string AlreadyReversedKg, IReadOnlyList<InventoryGoodsReceiptReversalLinkV1> Links);

public sealed record InventoryGoodsReceiptReversalLinkV1(Guid ConversionId, Guid FutureAllocationId,
    Guid SalesOrderId, Guid SalesOrderLineId, Guid PurchaseOrderGeneralLineId, string AllocationVersion,
    string OriginalQuantity, string OriginalKg, string AlreadyUndoneQuantity, Guid PhysicalAllocationId,
    int OriginalConsumptionSequence = 0);

/// <summary>
/// The physical kilograms are measured input, not a conversion of purchase quantity. The original link's
/// stamped quantity/kg pair is the only basis for a partial link undo. An approved return cost is supplied
/// by Purchasing's existing invoice/manual-exception flow; Inventory never chooses an exception policy.
/// </summary>
public sealed record InventoryGoodsReceiptReversalRequestV1(Guid CompanyId, Guid ReversalId, Guid ReversalLineId,
    Guid AcceptanceId, Guid InventoryMovementId, InventoryGoodsReceiptReversalKindV1 Kind,
    BlockState SourceBlock, string QuantityKg, string? OriginalUnitCostTryPerKg, string Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>Post-write Purchasing expectation. Its actual source must be bound to the SAME host transaction.</summary>
public sealed record InventoryGoodsReceiptReversalSupplyV1(Guid CompanyId, Guid PurchaseOrderId,
    Guid GeneralLineId, long OrderVersion, string NetOpenedQuantity, string AllocatableQuantity,
    string ImmediateReplacementQuantity = "0");

public sealed record InventoryGoodsReceiptConversionReversalV1(Guid ConversionId, Guid FutureAllocationId,
    string UndoneQuantity, string UndoneKg, string ReopenedQuantity, string OverflowReleasedQuantity,
    string UnheldQuantity);

public sealed record InventoryGoodsReceiptReversalResultV1(Guid CompanyId, Guid ReversalId, Guid AcceptanceId,
    Guid InventoryMovementId, Guid OriginalReceiptMovementId, Guid LabelId, string LabelVersion, string QuantityKg,
    IReadOnlyList<InventoryGoodsReceiptConversionReversalV1> Conversions, bool IsReplay, string OriginalCorrelationId);

public static class InventoryGoodsReceiptReversalCodesV1
{
    public const string CommandName = "inventory.goods-receipt-reversal.stage.v1";
    public const string EventType = "inventory.goods-receipt.reversed.v1";
    public const string SourceKind = "purchasing.acceptance-reversal";
    public const string NoSupplyReason = "acceptance-reversed-no-supply";
    public const string AlreadyFulfilled = "inventory_future_allocation_already_fulfilled";
    public const string InvalidRequest = "inventory_goods_receipt_reversal_invalid_request";
    public const string ExceedsReceipt = "inventory_goods_receipt_reversal_exceeds_receipt";
    public const string StockAllocated = "inventory_goods_receipt_reversal_stock_allocated";
    public const string StockNotAvailable = "inventory_goods_receipt_reversal_stock_not_available";
    public const string QuantityProfileUnavailable = "inventory_goods_receipt_reversal_quantity_profile_unavailable";
    public const string PartialQuantityNotRepresentable = "inventory_goods_receipt_reversal_partial_quantity_not_representable";
    public const string IdempotencyConflict = "inventory_goods_receipt_reversal_idempotency_conflict";
    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string Unavailable = "inventory_goods_receipt_reversal_unavailable";
    public static bool IsDefined(int status, string code) => status switch
    {
        400 or 404 => code == InvalidRequest,
        409 => code is AlreadyFulfilled or ExceedsReceipt or StockAllocated or StockNotAvailable or IdempotencyConflict or ConcurrentChange,
        422 => code == PartialQuantityNotRepresentable,
        503 => code is QuantityProfileUnavailable or Unavailable,
        _ => false,
    };
}

public sealed class InventoryGoodsReceiptReversalException : Exception
{
    public InventoryGoodsReceiptReversalException(int status, string code, Exception? inner = null) : base(code, inner)
    {
        if (!InventoryGoodsReceiptReversalCodesV1.IsDefined(status, code)) throw new ArgumentException("Unknown receipt reversal refusal.", nameof(code));
        Status = status; Code = code;
    }
    public int Status { get; }
    public string Code { get; }
}
