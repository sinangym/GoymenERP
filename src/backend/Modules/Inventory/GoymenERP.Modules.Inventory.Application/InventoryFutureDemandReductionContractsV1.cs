namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Owner-read schedule under the purchase line lock. Found with null means no dated unreceived slice.</summary>
public sealed record InventoryFutureSupplyScheduleV1(
    bool IsReadable, Guid PurchaseOrderId, Guid PurchaseOrderLineId, DateOnly? ExpectedDeliveryDate);

public interface IInventoryFutureSupplyScheduleSourceV1
{
    Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryUnavailableFutureSupplyScheduleSourceV1 : IInventoryFutureSupplyScheduleSourceV1
{
    public Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryFutureSupplyScheduleV1(false, purchaseOrderId, purchaseOrderLineId, null));
}

/// <summary>Explicit future selection is in the allocation's purchase unit. Physical selections remain fail-closed (KLN-D32).</summary>
public sealed record InventoryFutureDemandReleaseSelectionV1(Guid AllocationId, long ExpectedAllocationVersion,
    string QuantityText, bool IsPhysical = false);

/// <summary>
/// Server-owned Sales revision input; NewOpenQuantityText is new quantity minus shipped, never the raw browser quantity.
/// The caller holds every affected sales line lock in ascending order before calling, and owns commit/rollback/idempotency.
/// Null selections means latest expected purchase delivery first, then created time descending and allocation id ascending.
/// A non-null selection must account for exactly the excess; no implicit remainder is selected.
/// </summary>
public sealed record InventoryFutureDemandReductionRequestV1(Guid SalesOrderId, Guid SalesOrderLineId, Guid ProductId,
    string ProductVersion, Guid DemandUnitId, string NewOpenQuantityText, string ReasonText,
    IReadOnlyList<InventoryFutureDemandReleaseSelectionV1>? Selections = null);

public sealed record InventoryFutureDemandReductionResultV1(IReadOnlyList<InventoryFutureAllocationViewV1> ReleasedAllocations);
