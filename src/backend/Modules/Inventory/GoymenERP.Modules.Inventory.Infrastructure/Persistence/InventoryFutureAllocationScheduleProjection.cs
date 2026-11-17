using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>ALS-D90/F17-5: current owner dates decorate stored allocations without mutating them.
/// This is an advisory view, not the locked schedule used by allocation/release commands.</summary>
public static class InventoryFutureAllocationScheduleProjection
{
    public static async Task<IReadOnlyDictionary<Guid, InventoryFutureAllocationScheduleViewV1>> ReadAsync(
        IReadOnlyList<InventoryFutureAllocationViewV1> allocations, DateOnly evaluatedOn,
        IInventoryFutureSupplyScheduleSourceV1 source, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, InventoryFutureAllocationScheduleViewV1>();
        var sources = new Dictionary<(Guid Order, Guid Line), InventoryFutureAllocationScheduleViewV1>();
        foreach (var allocation in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (allocation.Status != "Active")
            {
                result.Add(allocation.AllocationId, new("Inactive", null, false));
                continue;
            }
            var key = (allocation.PurchaseOrderId, allocation.PurchaseOrderLineId);
            if (!sources.TryGetValue(key, out var view))
            {
                view = new("Unavailable", null, null);
                try
                {
                    var answer = await source.GetLineScheduleAsync(key.Item1, key.Item2, cancellationToken);
                    if (answer is { IsReadable: true } && answer.PurchaseOrderId == key.Item1
                        && answer.PurchaseOrderLineId == key.Item2)
                    {
                        if (answer.ExpectedDeliveryDate is { } date && date != default)
                            view = new("Scheduled", date, date < evaluatedOn);
                        else if (answer.ExpectedDeliveryDate is null)
                            view = new("Undated", null, false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { /* A failed owner read remains visibly unavailable. The final user fence still runs. */ }
                sources.Add(key, view);
            }
            result.Add(allocation.AllocationId, view);
        }
        return result;
    }
}
