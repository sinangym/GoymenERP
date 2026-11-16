using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using Microsoft.EntityFrameworkCore;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

internal sealed partial class InventoryFutureAllocationReleaseSession
{
    public async Task<InventoryFutureDemandReductionResultV1> ReduceDemandAsync(
        InventoryFutureDemandReductionRequestV1 request, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Engine.RequireMetadata(metadata);
        Engine.RequireCanonicalKeys(metadata);
        EnsureActive();
        if (request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty || request.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ProductVersion) || request.DemandUnitId == Guid.Empty
            || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(request.NewOpenQuantityText, out var open))
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        // Validate the reason before even a no-op reduction is accepted.
        var (_, reasonCode, reason) = Engine.ParseRelease(null,
            InventoryFutureAllocationReleaseReasonsV1.SalesLineReduced, request.ReasonText);

        // Sales owns its line locks; no Inventory read may race an allocation create on that demand.
        var active = await context.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryFutureAllocationKindsV1.SalesOrderLine
                && row.DemandId == request.SalesOrderId && row.DemandLineId == request.SalesOrderLineId
                && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .ToListAsync(cancellationToken);
        var boundLocks = purchaseLineLocks.Bind(connection, transaction);
        foreach (var lineId in active.Select(row => row.PurchaseOrderLineId).Distinct().Order())
            await boundLocks.AcquireAsync(lineId, cancellationToken);

        // Re-read under every purchase lock; consumed/released allocations no longer count.
        active = await context.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryFutureAllocationKindsV1.SalesOrderLine
                && row.DemandId == request.SalesOrderId && row.DemandLineId == request.SalesOrderLineId
                && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .ToListAsync(cancellationToken);
        var physicalKg = await context.Allocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                && row.DemandId == request.SalesOrderId && row.DemandLineId == request.SalesOrderLineId
                && row.Status == (int)StockAllocationStatus.Active)
            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
        if (active.Count == 0 && physicalKg == 0m && (request.Selections is null || request.Selections.Count == 0))
            return new([]);
        var openKg = await ToKilogramsAsync(request.ProductId, request.ProductVersion, open.Value,
            request.DemandUnitId, cancellationToken);
        var candidates = new List<FutureSupplyReductionAllocation>();
        // ALL schedules/conversions precede terminal selection/physical refusals, so unreadable source remains retryable.
        foreach (var row in active.OrderBy(row => row.PurchaseOrderLineId).ThenBy(row => row.Id))
        {
            if (row.ProductId != request.ProductId)
                throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
            var schedule = await schedules.GetLineScheduleAsync(row.PurchaseOrderId, row.PurchaseOrderLineId, cancellationToken);
            if (schedule is null || !schedule.IsReadable || schedule.PurchaseOrderId != row.PurchaseOrderId
                || schedule.PurchaseOrderLineId != row.PurchaseOrderLineId)
                throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
            var factor = await ToKilogramsAsync(row.ProductId, row.ProductVersion, 1m, row.PurchaseUnitId, cancellationToken);
            if (factor <= 0m) throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
            candidates.Add(new(row.Id, row.Version, row.RemainingQuantity, factor, schedule.ExpectedDeliveryDate, row.CreatedAtUtc));
        }
        IReadOnlyList<FutureSupplyReductionSelection>? selections = null;
        if (request.Selections is not null)
        {
            var parsed = new List<FutureSupplyReductionSelection>();
            foreach (var selection in request.Selections)
            {
                if (selection is null || !InventoryFutureAllocationTextV1.TryPositiveQuantity(selection.QuantityText, out var quantity))
                    throw Engine.Error(422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid);
                if (!selection.IsPhysical)
                {
                    var selected = await context.FutureSupplyAllocations.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == selection.AllocationId, cancellationToken)
                        ?? throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference);
                    if (selected.DemandId != request.SalesOrderId || selected.DemandLineId != request.SalesOrderLineId)
                        throw Engine.Error(422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid);
                    if (selected.Version != selection.ExpectedAllocationVersion || selected.Status != (int)FutureSupplyAllocationStatus.Active)
                        throw Engine.Error(409, InventoryFutureAllocationCodesV1.VersionConflict);
                }
                parsed.Add(new(selection.AllocationId, selection.ExpectedAllocationVersion, quantity.Value, selection.IsPhysical));
            }
            selections = parsed;
        }
        IReadOnlyList<FutureSupplyReductionStep> plan;
        try { plan = FutureSupplyDemandReduction.Plan(openKg, physicalKg, candidates, selections); }
        catch (FutureSupplyReductionException exception)
        {
            throw exception.Failure switch
            {
                FutureSupplyReductionFailure.VersionConflict => Engine.Error(409, InventoryFutureAllocationCodesV1.VersionConflict, exception),
                FutureSupplyReductionFailure.PhysicalReleaseUnavailable => Engine.Error(409, InventoryFutureAllocationCodesV1.PhysicalReleaseUnavailable, exception),
                FutureSupplyReductionFailure.ConversionUnavailable => Engine.Error(422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable, exception),
                _ => Engine.Error(422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid, exception),
            };
        }
        var released = new List<InventoryFutureAllocationViewV1>();
        foreach (var step in plan)
        {
            released.Add(await ReleaseAsync(new(step.Id, step.ExpectedVersion,
                step.Quantity.ToString(CultureInfo.InvariantCulture), reasonCode, reason, InventoryFutureAllocationOriginsV1.Sales),
                metadata, cancellationToken));
        }
        return new(released);
    }

    private async Task<decimal> ToKilogramsAsync(Guid productId, string productVersion, decimal quantity, Guid unitId,
        CancellationToken cancellationToken)
    {
        var answer = await unitConverter.ConvertToKilogramsAsync(productId, productVersion, quantity, unitId, cancellationToken);
        if (answer is not { Outcome: InventoryFutureUnitConversionOutcomeV1.Converted, Quantity: >= 0m })
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        return answer.Quantity.Value;
    }
}
