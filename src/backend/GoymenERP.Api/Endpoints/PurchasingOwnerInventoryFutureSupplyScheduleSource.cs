using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KLN-D30 / ALS-D81: translate the owner's remaining schedule, never redistribute accepted receipts or infer a date from an
/// unreadable source. The caller holds the purchase-line lock. This owner read uses Purchasing's scoped connection; only
/// Sales and Inventory mutation enlistment share the Sales DbTransaction. Schedule writers must share the purchase lock.
/// </summary>
public sealed class PurchasingOwnerInventoryFutureSupplyScheduleSource(
    IPurchaseFutureSupplyScheduleReadPortV1 purchasing, ICompanyContext companyContext) : IInventoryFutureSupplyScheduleSourceV1
{
    public async Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        if (companyId == Guid.Empty || purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty) return Unreadable();
        try
        {
            var answer = await purchasing.GetLineScheduleAsync(purchaseOrderId, purchaseOrderLineId, cancellationToken);
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId || answer is null
                || answer.Outcome != PurchaseFutureSupplyScheduleOutcomeV1.Found || answer.CompanyId != companyId
                || answer.OrderId != purchaseOrderId || answer.LineId != purchaseOrderLineId || answer.UnitId == Guid.Empty
                || answer.ObservedOrderVersion is null or < 1 || answer.Slices is null) return Unreadable();
            DateOnly? latestRemaining = null;
            var identities = new HashSet<Guid>();
            var sequence = new HashSet<int>();
            foreach (var slice in answer.Slices)
            {
                if (slice is null || slice.SliceId == Guid.Empty || !identities.Add(slice.SliceId)
                    || slice.SliceNo < 1 || !sequence.Add(slice.SliceNo) || slice.DeliveryDate == default
                    || !InventoryFutureAllocationTextV1.TryPositiveQuantity(slice.QuantityText, out var quantity)
                    || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(slice.RemainingText, out var remaining)
                    || remaining.Value > quantity.Value) return Unreadable();
                if (remaining.Value > 0m && (latestRemaining is null || slice.DeliveryDate > latestRemaining))
                    latestRemaining = slice.DeliveryDate;
            }
            return new(true, purchaseOrderId, purchaseOrderLineId, latestRemaining);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unreadable(); }

        InventoryFutureSupplyScheduleV1 Unreadable() => new(false, purchaseOrderId, purchaseOrderLineId, null);
    }
}
