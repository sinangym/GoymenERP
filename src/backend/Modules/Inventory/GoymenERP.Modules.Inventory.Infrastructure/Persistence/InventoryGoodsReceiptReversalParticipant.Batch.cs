using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;
using Codes = GoymenERP.Modules.Inventory.Application.InventoryGoodsReceiptReversalCodesV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryGoodsReceiptReversalParticipant
{
    /// <summary>
    /// API-44: load every awaited source first, then evaluate the complete correction against shared shadow budgets.
    /// This method never tracks or saves business entities, stages movements, enqueues audit or uses a savepoint.
    /// It first takes the technical Inventory validation guard in the already borrowed host transaction.
    /// A later unavailable owner therefore dominates an earlier stable business refusal.
    /// </summary>
    public async Task EvaluateBatchAsync(IReadOnlyList<InventoryGoodsReceiptReversalBatchLineV1> lines,
        CancellationToken cancellationToken = default)
    {
        try { await EvaluateBatchCoreAsync(lines, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (InventoryGoodsReceiptReversalException) { throw; }
        catch (Exception exception) { throw Error(503, Codes.Unavailable, exception); }
    }

    private async Task EvaluateBatchCoreAsync(IReadOnlyList<InventoryGoodsReceiptReversalBatchLineV1> lines,
        CancellationToken cancellationToken)
    {
        EnsureActive(); ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(line => line is null)
            || lines.Select(line => line.Request.AcceptanceId).Distinct().Count() != lines.Count)
            throw Error(400, Codes.InvalidRequest);
        await store.LockPhysicalValidationAsync(cancellationToken);
        var targets = new List<BatchRead>();
        var demandsByLine = new Dictionary<Guid, BatchDemand>();
        var supplyByLine = new Dictionary<Guid, BatchSupply>();
        var physicalById = new Dictionary<Guid, AllocationRow>();
        var labels = new Dictionary<Guid, BatchLabel>();
        foreach (var item in lines)
        {
            Validate(item.Request, item.Metadata);
            // A fresh Purchasing batch cannot contain a separately committed Inventory child command.
            var existing = await ReadReplayAsync(item.Request, item.Metadata, cancellationToken);
            var target = await ReadTargetAsync(item.Request.AcceptanceId, cancellationToken) ?? throw Error(503, Codes.Unavailable);
            var original = target.Original.OriginalRequest;
            await store.ObservePhysicalCountBoundaryAsync(original.ProductId, original.LocationId, cancellationToken);
            var expected = item.ExpectedSupply;
            if (item.Request.Kind != InventoryGoodsReceiptReversalKindV1.AcceptanceUndo
                || expected.CompanyId != companyId || expected.PurchaseOrderId != original.PurchaseOrderId
                || expected.GeneralLineId == Guid.Empty || expected.OrderVersion < 1)
                throw Error(503, Codes.Unavailable);
            var amount = Quantity(expected.AllocatableQuantity); var opened = Quantity(expected.NetOpenedQuantity);
            var replacement = Quantity(expected.ImmediateReplacementQuantity);
            if (supplyByLine.TryGetValue(expected.GeneralLineId, out var prior))
            {
                if (prior.Expected != expected || prior.ProductId != original.ProductId || prior.ProductVersion != original.ExpectedProductVersion
                    || prior.UnitId != original.PurchaseUnitId) throw Error(503, Codes.Unavailable);
            }
            else
            {
                var actual = await supply.GetAllocatableLineAsync(original.PurchaseOrderId, expected.GeneralLineId, cancellationToken);
                if (actual is null || !Enum.IsDefined(actual.Outcome)
                    || actual.Outcome is InventoryFutureSupplySourceOutcomeV1.Unavailable or InventoryFutureSupplySourceOutcomeV1.NotFound
                    || actual.Line is not { } source || source.OrderId != original.PurchaseOrderId || source.LineId != expected.GeneralLineId
                    || source.ProductId != original.ProductId || source.ProductVersion != original.ExpectedProductVersion
                    || source.UnitId != original.PurchaseUnitId || source.ObservedOrderVersion != expected.OrderVersion
                    || Quantity(source.AllocatableText) != amount || opened > 0m && actual.Outcome != InventoryFutureSupplySourceOutcomeV1.Allocatable)
                    throw Error(503, Codes.Unavailable);
                var active = await context.FutureSupplyAllocations.AsNoTracking().Where(row => row.PurchaseOrderLineId == expected.GeneralLineId
                    && row.Status == (int)FutureSupplyAllocationStatus.Active).SumAsync(row => (decimal?)row.RemainingQuantity, cancellationToken) ?? 0m;
                var ledger = await context.FutureSupplyLines.AsNoTracking().SingleOrDefaultAsync(row => row.PurchaseOrderLineId == expected.GeneralLineId, cancellationToken);
                if (target.Links.Count > 0 && (ledger is null || ledger.ActiveAllocated != active)) throw Error(503, Codes.Unavailable);
                supplyByLine.Add(expected.GeneralLineId, new(expected, original.ProductId, original.ExpectedProductVersion, original.PurchaseUnitId,
                    Math.Min(opened + replacement, Math.Max(0m, amount + replacement - active))));
            }

            var scale = await new InventoryKilogramScale(settings).CheckAsync(companyId,
                DateOnly.FromDateTime(item.Request.OccurredAtUtc.UtcDateTime), [Kilograms.Positive(Quantity(item.Request.QuantityKg))], cancellationToken);
            if (!scale.IsAccepted) throw StoreRefusal(scale.Code!);
            foreach (var link in target.Links)
            {
                if (!demandsByLine.ContainsKey(link.SalesOrderLineId))
                {
                    var answer = await demands.GetDemandLineAsync(link.SalesOrderId, link.SalesOrderLineId, cancellationToken);
                    if (answer is null || !Enum.IsDefined(answer.Outcome)
                        || answer.Outcome is InventoryFutureDemandSourceOutcomeV1.Unavailable or InventoryFutureDemandSourceOutcomeV1.NotFound)
                        throw Error(503, Codes.Unavailable);
                    decimal open = 0m, held = 0m;
                    if (answer.Outcome == InventoryFutureDemandSourceOutcomeV1.Open)
                    {
                        if (answer.Line is not { } demand || demand.OrderId != link.SalesOrderId || demand.LineId != link.SalesOrderLineId
                            || demand.ProductId != original.ProductId || string.IsNullOrWhiteSpace(demand.ProductVersion) || demand.UnitId == Guid.Empty)
                            throw Error(503, Codes.Unavailable);
                        open = await ToKgAsync(demand.ProductId, demand.ProductVersion, Quantity(demand.OpenQuantityText), demand.UnitId, cancellationToken);
                        held = await context.Allocations.AsNoTracking().Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                            && row.DemandId == link.SalesOrderId && row.DemandLineId == link.SalesOrderLineId
                            && row.Status == (int)StockAllocationStatus.Active).SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
                        var future = await context.FutureSupplyAllocations.AsNoTracking().Where(row => row.DemandId == link.SalesOrderId
                            && row.DemandLineId == link.SalesOrderLineId && row.Status == (int)FutureSupplyAllocationStatus.Active).ToListAsync(cancellationToken);
                        foreach (var row in future) held += await ToKgAsync(row.ProductId, row.ProductVersion, row.RemainingQuantity, row.PurchaseUnitId, cancellationToken);
                    }
                    demandsByLine.Add(link.SalesOrderLineId, new(link.SalesOrderId, answer.Outcome == InventoryFutureDemandSourceOutcomeV1.Open, open, held));
                }
                else if (demandsByLine[link.SalesOrderLineId].OrderId != link.SalesOrderId) throw Error(503, Codes.Unavailable);
                var allocation = await context.FutureSupplyAllocations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == link.FutureAllocationId, cancellationToken);
                if (allocation is null || allocation.PurchaseOrderLineId != expected.GeneralLineId || allocation.ProductId != original.ProductId
                    || allocation.PurchaseUnitId != original.PurchaseUnitId) throw Error(503, Codes.Unavailable);
                Engine.RestoreStored(allocation);
                var physical = await context.Allocations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == link.PhysicalAllocationId, cancellationToken);
                if (physical is null || physical.LabelId != original.LabelId || physical.DemandId != link.SalesOrderId
                    || physical.DemandLineId != link.SalesOrderLineId || !Enum.IsDefined((StockAllocationStatus)physical.Status)) throw Error(503, Codes.Unavailable);
                physicalById.TryAdd(physical.Id, physical);
            }
            if (!labels.ContainsKey(original.LabelId))
            {
                var total = await context.Balances.AsNoTracking().Where(row => row.LabelId == original.LabelId)
                    .SumAsync(row => (decimal?)row.QuantityKg, cancellationToken) ?? 0m;
                var allocated = await context.Allocations.AsNoTracking().Where(row => row.LabelId == original.LabelId && row.Status == (int)StockAllocationStatus.Active)
                    .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
                var issued = await (from movement in context.Movements.AsNoTracking()
                                    join line in context.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
                                    where line.LabelId == original.LabelId && movement.Kind == (int)StockMovementKind.Issue select line.MovementId).AnyAsync(cancellationToken);
                labels.Add(original.LabelId, new(total, allocated, issued));
            }
            var position = await context.Balances.AsNoTracking().Where(row => row.LabelId == original.LabelId
                && row.OwnerKind == (int)original.OwnerKind && row.OwnerId == original.OwnerId
                && row.CustodianKind == (int)original.CustodianKind && row.CustodianId == original.CustodianId
                && row.LocationId == original.LocationId && row.Block == (int)item.Request.SourceBlock)
                .SumAsync(row => (decimal?)row.QuantityKg, cancellationToken) ?? 0m;
            targets.Add(new(item, target, position, existing is not null));
        }

        // No awaited owner read follows this point. Pure shadow changes mirror StageCore's original stamped-ratio kernel.
        var positions = new Dictionary<(Guid LabelId, int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId, Guid LocationId, int Block), decimal>();
        var remainingPhysical = physicalById.ToDictionary(pair => pair.Key,
            pair => pair.Value.Status == (int)StockAllocationStatus.Active ? pair.Value.RemainingKg : 0m);
        InventoryGoodsReceiptReversalException? terminal = null;
        foreach (var read in targets)
        {
            try
            {
                var item = read.Line; var target = read.Target; var original = target.Original.OriginalRequest;
                var undo = Quantity(item.Request.QuantityKg); var label = labels[original.LabelId];
                if (read.Existing) throw Error(409, Codes.IdempotencyConflict);
                if (Quantity(target.AlreadyReversedKg) + undo > Quantity(original.QuantityKg)) throw Error(409, Codes.ExceedsReceipt);
                if (label.Issued || target.Links.Any(link => physicalById[link.PhysicalAllocationId].Status == (int)StockAllocationStatus.Consumed))
                    throw Error(409, Codes.AlreadyFulfilled);
                var supplyBudget = supplyByLine[item.ExpectedSupply.GeneralLineId]; var remaining = undo;
                foreach (var link in target.Links.OrderByDescending(link => link.OriginalConsumptionSequence))
                {
                    if (remaining == 0m) break;
                    var quantity = Quantity(link.OriginalQuantity); var kg = Quantity(link.OriginalKg); var undone = Quantity(link.AlreadyUndoneQuantity);
                    var take = Math.Min(remaining, FutureSupplyReceiptReversal.ExactRatio(quantity - undone, kg, quantity));
                    if (take == 0m) continue;
                    var activeKg = remainingPhysical[link.PhysicalAllocationId]; var released = Math.Min(take, activeKg);
                    var demand = demandsByLine[link.SalesOrderLineId];
                    if (demand.Open) demand.Held -= released;
                    var plan = FutureSupplyReceiptReversal.Plan(quantity, kg, undone, take, activeKg, demand.Open,
                        demand.Open ? Math.Max(0m, demand.Quantity - demand.Held) : 0m, supplyBudget.Remaining);
                    remainingPhysical[link.PhysicalAllocationId] -= released; label.Allocated -= released;
                    supplyBudget.Remaining -= plan.ReopenedQuantity;
                    if (demand.Open) demand.Held += FutureSupplyReceiptReversal.ExactRatio(plan.ReopenedQuantity, kg, quantity);
                    remaining -= take;
                }
                var key = (original.LabelId, (int)original.OwnerKind, original.OwnerId, (int)original.CustodianKind,
                    original.CustodianId, original.LocationId, (int)item.Request.SourceBlock);
                if (!positions.TryGetValue(key, out var position)) position = read.Position;
                if (position < undo)
                {
                    var physicalPosition = new StockPosition(companyId, new StockOwner(original.OwnerKind, original.OwnerId),
                        new StockCustodian(original.CustodianKind, original.CustodianId), original.LocationId,
                        new MaterialLabel(original.LabelId, companyId, original.LabelCode, original.ProductId,
                            original.LotId, original.ExpectedProductVersion), item.Request.SourceBlock);
                    var movement = new StockMovement(item.Request.InventoryMovementId, companyId,
                        StockMovementKind.PurchaseAcceptanceReversal, item.Request.OccurredAtUtc,
                        new StockSourceReference(Codes.SourceKind, original.MovementId, item.Request.ReversalLineId),
                        [new StockMovementLine(physicalPosition, StockDirection.Out, Kilograms.Positive(undo))], CostResult.NotCalculated);
                    throw Error(409, Codes.StockNotAvailable, store.PhysicalStockRefusal(movement,
                        new StockShortfall(movement.Kind, movement.Source, physicalPosition, Kilograms.Positive(undo),
                            Kilograms.Of(position), 1), item.Metadata, original.LabelCode));
                }
                if (label.Allocated > label.Physical - undo) throw Error(409, Codes.StockAllocated);
                positions[key] = position - undo; label.Physical -= undo;
            }
            catch (InventoryGoodsReceiptReversalException exception) { terminal ??= exception; }
            catch (FutureSupplyReceiptReversalPrecisionException exception) { terminal ??= Error(422, Codes.PartialQuantityNotRepresentable, exception); }
            catch (ArgumentException exception) { throw Error(503, Codes.Unavailable, exception); }
            catch (OverflowException exception) { throw Error(503, Codes.Unavailable, exception); }
        }
        if (terminal is not null) throw terminal;
    }

    private sealed record BatchRead(InventoryGoodsReceiptReversalBatchLineV1 Line, InventoryGoodsReceiptReversalTargetV1 Target,
        decimal Position, bool Existing);
    private sealed class BatchDemand(Guid orderId, bool open, decimal quantity, decimal held)
    { public Guid OrderId { get; } = orderId; public bool Open { get; } = open; public decimal Quantity { get; } = quantity; public decimal Held { get; set; } = held; }
    private sealed class BatchSupply(InventoryGoodsReceiptReversalSupplyV1 expected, Guid productId, string productVersion, Guid unitId, decimal remaining)
    {
        public InventoryGoodsReceiptReversalSupplyV1 Expected { get; } = expected;
        public Guid ProductId { get; } = productId; public string ProductVersion { get; } = productVersion; public Guid UnitId { get; } = unitId;
        public decimal Remaining { get; set; } = remaining;
    }
    private sealed class BatchLabel(decimal physical, decimal allocated, bool issued)
    { public decimal Physical { get; set; } = physical; public decimal Allocated { get; set; } = allocated; public bool Issued { get; } = issued; }
}
