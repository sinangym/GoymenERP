using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// STK-S17 / F017-c: conversion moves quantity between the two sides of the Sales physical+future cap. It must share the Sales
/// serialization resource with BOTH allocation creators, including creates against another purchase line. Resolve and read
/// before taking any purchase lock; never acquire a previously unknown Sales lock after a purchase lock. A false answer means
/// the caller must discard this entire PREWRITE transaction and prepare again, without invoking the business callback.
/// </summary>
internal static class PurchaseGoodsReceiptPrelock
{
    internal const int MaximumPreparationAttempts = 3;

    internal static void ValidateIntent(Guid companyId, PurchaseGoodsReceiptTargetV1 target)
    {
        if (companyId == Guid.Empty || target is null || target.PurchaseOrderId == Guid.Empty
            || target.PurchaseOrderLineId == Guid.Empty
            || !InventoryFutureAllocationTextV1.TryVersion(target.ExpectedPurchaseOrderVersion, out _))
            throw new PurchasingServiceException(400, PurchaseAcceptanceParticipantCodes.InvalidRequest);
    }

    internal static async Task<bool> PrepareAsync(Guid companyId, PurchaseGoodsReceiptTargetV1 intent,
        Func<CancellationToken, Task<PurchaseAcceptanceReceiptTargetV1?>> resolveTarget,
        IInventoryFutureAllocationReleaseSessionV1 inventory, ISalesOrderLineSerializationLockV1 salesLocks,
        IPurchaseLineSerializationLockV1 purchaseLocks, CancellationToken cancellationToken)
    {
        ValidateIntent(companyId, intent);
        var target = RequireTarget(await resolveTarget(cancellationToken));
        var initial = await ReadActiveAsync(target);
        var salesIds = initial.Select(view => view.SalesOrderLineId).ToHashSet();
        foreach (var line in salesIds.Order()) await salesLocks.AcquireAsync(line, cancellationToken);
        // A receipt/batch has one target general line; the complete purchase union is locked before ANY acceptance or label.
        await purchaseLocks.AcquireAsync(target.PurchaseOrderGeneralLineId, cancellationToken);
        var current = RequireTarget(await resolveTarget(cancellationToken));
        if (current.PurchaseOrderGeneralLineId != target.PurchaseOrderGeneralLineId) return false;
        var initialIds = initial.Select(view => view.AllocationId).ToHashSet();
        var fresh = await ReadActiveAsync(current);
        // A creator may have committed after our first read and before its purchase lock. Never silently skip that Sales lock,
        // nor append it out of order. Even a new row on an already locked Sales line gets a fresh prewrite preparation.
        return fresh.All(view => initialIds.Contains(view.AllocationId) && salesIds.Contains(view.SalesOrderLineId));

        PurchaseAcceptanceReceiptTargetV1 RequireTarget(PurchaseAcceptanceReceiptTargetV1? answer)
        {
            if (answer is null) throw new PurchasingServiceException(404, PurchaseAcceptanceParticipantCodes.InvalidReference);
            if (answer.CompanyId != companyId || answer.PurchaseOrderId != intent.PurchaseOrderId
                || answer.PurchaseOrderLineId != intent.PurchaseOrderLineId || answer.PurchaseOrderGeneralLineId == Guid.Empty
                || !InventoryFutureAllocationTextV1.TryVersion(answer.ObservedOrderVersion, out _)) throw Unavailable();
            return answer;
        }

        async Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ReadActiveAsync(PurchaseAcceptanceReceiptTargetV1 source)
        {
            var rows = await inventory.ListActiveByPurchaseLineAsync(source.PurchaseOrderGeneralLineId, cancellationToken);
            if (rows is null || rows.Any(view => view is null || view.CompanyId != companyId
                    || view.PurchaseOrderId != source.PurchaseOrderId || view.PurchaseOrderLineId != source.PurchaseOrderGeneralLineId
                    || view.AllocationId == Guid.Empty || view.SalesOrderId == Guid.Empty || view.SalesOrderLineId == Guid.Empty
                    || view.Status != nameof(FutureSupplyAllocationStatus.Active)
                    || !InventoryFutureAllocationTextV1.TryVersion(view.Version, out _)
                    || !InventoryFutureAllocationTextV1.TryPositiveQuantity(view.RemainingQuantity, out _))
                || rows.Select(view => view.AllocationId).Distinct().Count() != rows.Count) throw Unavailable();
            return rows;
        }
    }

    internal static PurchasingServiceException Unavailable() => new(503, PurchaseGoodsReceiptCodesV1.SourceInvalid);
    internal static PurchasingServiceException RetryExhausted() => new(409, PurchaseAcceptanceParticipantCodes.ConcurrentChange);
}
