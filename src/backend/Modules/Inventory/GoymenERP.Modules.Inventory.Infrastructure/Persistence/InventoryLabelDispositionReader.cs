using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Scraps;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-009 D1: the one computation of a label's disposition, shared by <see cref="InventoryLabelDispositionReadPort"/>,
/// <see cref="InventoryFreeQuantityReadPort"/> and the output-reversal participant, so the figure Manufacturing shows
/// and the figure the reversal command enforces are the same code. It reads Inventory's own rows only, always under an
/// explicit company predicate, and writes nothing.
/// <para>
/// Fail-closed: a movement line that breaks its kind's shape, a balance that does not reconcile with the label's
/// movements, two output receipts on one label or a reversal without its output reads as
/// <see cref="InventoryLabelDispositionStatusV1.Unreadable"/>; no figure is guessed.
/// </para>
/// </summary>
internal static class InventoryLabelDispositionReader
{
    /// <summary>The definite-shipment participant's source kind (Logistics owns the shipment; Inventory its Issue).</summary>
    internal const string DefiniteShipmentSourceKind = "logistics.definite-shipment";

    internal static async Task<InventoryLabelDispositionSnapshot> ReadAsync(InventoryDbContext context,
        Guid companyId, Guid labelId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || labelId == Guid.Empty)
        {
            return InventoryLabelDispositionSnapshot.NotFound;
        }

        var label = await context.Labels.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Id == labelId)
            .Select(row => new { row.Id, row.ProductId, row.Version })
            .SingleOrDefaultAsync(cancellationToken);
        if (label is null)
        {
            return InventoryLabelDispositionSnapshot.NotFound;
        }

        var lines = await (
                from line in context.MovementLines.AsNoTracking()
                join movement in context.Movements.AsNoTracking()
                    on new { line.CompanyId, Id = line.MovementId } equals new { movement.CompanyId, movement.Id }
                where line.CompanyId == companyId && line.LabelId == labelId
                orderby movement.RecordedAtUtc, movement.Id, line.LineNumber
                select new DispositionLineFact(movement.Id, movement.Kind, movement.SourceKind, movement.SourceId,
                    movement.SourceLineId, line.Direction, line.OwnerKind, line.OwnerId, line.CustodianKind,
                    line.CustodianId, line.LocationId, line.ProductId, line.Block, line.QuantityKg))
            .ToListAsync(cancellationToken);
        var balances = await context.Balances.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.LabelId == labelId)
            .Select(row => new DispositionBalanceFact(row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId,
                row.LocationId, row.ProductId, row.Block, row.QuantityKg))
            .ToListAsync(cancellationToken);
        var allocations = await context.Allocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.LabelId == labelId
                && row.Status == (int)StockAllocationStatus.Active)
            .Select(row => new DispositionBalanceFact(row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId,
                row.LocationId, label.ProductId, row.Block, row.RemainingKg))
            .ToListAsync(cancellationToken);
        return Compute(label.Id, label.ProductId, label.Version, lines, balances, allocations);
    }

    /// <summary>The pure part, separated from the queries so the reconciliation rules have one place.</summary>
    internal static InventoryLabelDispositionSnapshot Compute(Guid labelId, Guid productId, long labelVersion,
        IReadOnlyList<DispositionLineFact> lines, IReadOnlyList<DispositionBalanceFact> balances, IReadOnlyList<DispositionBalanceFact> allocations)
    {
        var totals = new Totals();
        var netKg = 0m;
        var outputReceipts = new List<DispositionLineFact>();
        var reversals = new List<DispositionLineFact>();
        foreach (var line in lines)
        {
            var kind = (StockMovementKind)line.Kind;
            if (!Enum.IsDefined(kind) || line.Direction is not ((int)StockDirection.In or (int)StockDirection.Out)
                || line.Block is not ((int)BlockState.Free or (int)BlockState.Blocked) || line.QuantityKg <= 0m
                || line.ProductId != productId || !ShapeMatches(kind, (StockDirection)line.Direction))
            {
                return InventoryLabelDispositionSnapshot.Unreadable;
            }

            var into = line.Direction == (int)StockDirection.In;
            netKg += into ? line.QuantityKg : -line.QuantityKg;
            Classify(kind, line, into, totals, outputReceipts, reversals);
        }

        if (!ValidBalances(balances, productId, out var physicalKg, out var blockedKg) || physicalKg != netKg
            || allocations.Any(allocation => allocation.QuantityKg < 0m
                || allocation.Block is not ((int)BlockState.Free or (int)BlockState.Blocked)))
        {
            return InventoryLabelDispositionSnapshot.Unreadable;
        }

        if (!TryOutput(outputReceipts, reversals, balances, allocations, out var output))
        {
            return InventoryLabelDispositionSnapshot.Unreadable;
        }

        var freePositions = balances
            .Where(balance => balance.Block == (int)BlockState.Free && balance.QuantityKg > 0m)
            .ToArray();
        var freeUnallocatedKg = 0m;
        foreach (var free in freePositions)
        {
            freeUnallocatedKg += Unreserved(free.QuantityKg, AllocatedAt(allocations, free.Key, BlockState.Free));
        }

        return new InventoryLabelDispositionSnapshot(InventoryLabelDispositionStatusV1.Value, labelId, productId,
            labelVersion, output, totals.ShippedKg, totals.CustomerReturnedKg, totals.ManufacturingIssuedKg,
            totals.ManufacturingReturnedKg, totals.TransferDispatchedKg, totals.TransferAcceptedKg,
            totals.ScrappedKg, totals.CountAdjustedInKg, totals.CountAdjustedOutKg, totals.OtherInKg,
            totals.OtherOutKg, physicalKg, blockedKg, allocations.Sum(allocation => allocation.QuantityKg),
            freeUnallocatedKg, freePositions.Length);
    }

    private static bool ShapeMatches(StockMovementKind kind, StockDirection direction) => kind switch
    {
        StockMovementKind.Receipt or StockMovementKind.Return or StockMovementKind.TransferAccept
            or StockMovementKind.TransferDispatchCancellation
            or StockMovementKind.TransferReceiptOverage => direction == StockDirection.In,
        StockMovementKind.Issue or StockMovementKind.Scrap or StockMovementKind.TransferDispatch
            or StockMovementKind.ManufacturingOutputReversal
            or StockMovementKind.TransferReceiptShortage
            or StockMovementKind.SupplierReturnExit => direction == StockDirection.Out,
        _ => true,
    };

    private static void Classify(StockMovementKind kind, DispositionLineFact line, bool into, Totals totals,
        List<DispositionLineFact> outputReceipts, List<DispositionLineFact> reversals)
    {
        var quantity = line.QuantityKg;
        switch (kind)
        {
            // A block change is one Out and one In on the same position: it moves no physical kg.
            case StockMovementKind.Block or StockMovementKind.Release:
                return;
            case StockMovementKind.Receipt
                when line.SourceKind == InventoryManufacturingRealizationContractV1.OutputSourceKind:
                outputReceipts.Add(line);
                return;
            case StockMovementKind.ManufacturingOutputReversal:
                reversals.Add(line);
                return;
            case StockMovementKind.Issue when line.SourceKind == DefiniteShipmentSourceKind:
                totals.ShippedKg += quantity;
                return;
            case StockMovementKind.Return when line.SourceKind == InventoryCustomerReturnContractV1.SourceKind:
                totals.CustomerReturnedKg += quantity;
                return;
            case StockMovementKind.Issue
                when line.SourceKind == InventoryManufacturingRealizationContractV1.InputIssueSourceKind:
                totals.ManufacturingIssuedKg += quantity;
                return;
            case StockMovementKind.Return
                when line.SourceKind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind:
                totals.ManufacturingReturnedKg += quantity;
                return;
            case StockMovementKind.TransferDispatch when line.SourceKind == InventoryTransfer.SourceKind:
                totals.TransferDispatchedKg += quantity;
                return;
            case StockMovementKind.TransferAccept when line.SourceKind == InventoryTransfer.SourceKind:
                totals.TransferAcceptedKg += quantity;
                return;
            case StockMovementKind.Scrap when line.SourceKind == StockScrap.SourceKind:
                totals.ScrappedKg += quantity;
                return;
            case StockMovementKind.CountAdjustment when line.SourceKind == StockCount.SourceKind:
                if (into)
                {
                    totals.CountAdjustedInKg += quantity;
                }
                else
                {
                    totals.CountAdjustedOutKg += quantity;
                }

                return;
        }

        if (into)
        {
            totals.OtherInKg += quantity;
        }
        else
        {
            totals.OtherOutKg += quantity;
        }
    }

    /// <summary>
    /// The stored balances are a valid state (the domain ledger's own rule): one bucket per position and block, no
    /// negative bucket, the label's product, and every non-zero bucket in one physical place.
    /// </summary>
    private static bool ValidBalances(IReadOnlyList<DispositionBalanceFact> balances, Guid productId, out decimal physicalKg,
        out decimal blockedKg)
    {
        physicalKg = 0m;
        blockedKg = 0m;
        var seen = new HashSet<(DispositionPositionKey, int)>();
        foreach (var balance in balances)
        {
            if (balance.QuantityKg < 0m || balance.ProductId != productId
                || balance.Block is not ((int)BlockState.Free or (int)BlockState.Blocked)
                || !seen.Add((balance.Key, balance.Block)))
            {
                return false;
            }

            physicalKg += balance.QuantityKg;
            if (balance.Block == (int)BlockState.Blocked)
            {
                blockedKg += balance.QuantityKg;
            }
        }

        return balances.Where(balance => balance.QuantityKg != 0m).Select(balance => balance.Key).Distinct()
            .Count() <= 1;
    }

    /// <summary>
    /// An output label has at most one manufacturing output receipt, of exactly one line, naming its output. Every
    /// reversal on the label belongs to that receipt's order, and together they never exceed what it received.
    /// </summary>
    private static bool TryOutput(IReadOnlyList<DispositionLineFact> receipts, IReadOnlyList<DispositionLineFact> reversals,
        IReadOnlyList<DispositionBalanceFact> balances, IReadOnlyList<DispositionBalanceFact> allocations,
        out InventoryLabelOutputSnapshot? output)
    {
        output = null;
        if (receipts.Count == 0)
        {
            return reversals.Count == 0;
        }

        var receipt = receipts[0];
        if (receipts.Count != 1 || receipt.SourceLineId is not { } outputId || outputId == Guid.Empty
            || reversals.Any(reversal => reversal.SourceKind
                    != InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind
                || reversal.SourceId != receipt.SourceId))
        {
            return false;
        }

        var reversedKg = reversals.Sum(reversal => reversal.QuantityKg);
        if (reversedKg > receipt.QuantityKg)
        {
            return false;
        }

        var position = receipt.Key;
        var freeKg = balances.Where(balance => balance.Key == position && balance.Block == (int)BlockState.Free)
            .Sum(balance => balance.QuantityKg);
        var blockedKg = balances
            .Where(balance => balance.Key == position && balance.Block == (int)BlockState.Blocked)
            .Sum(balance => balance.QuantityKg);
        var allocatedKg = AllocatedAt(allocations, position, BlockState.Free);
        output = new InventoryLabelOutputSnapshot(receipt.SourceId, outputId, receipt.MovementId, position,
            receipt.QuantityKg, reversedKg, freeKg, allocatedKg, Unreserved(freeKg, allocatedKg), blockedKg);
        return true;
    }

    private static decimal AllocatedAt(IReadOnlyList<DispositionBalanceFact> allocations, DispositionPositionKey position,
        BlockState block) => allocations
        .Where(allocation => allocation.Key == position && allocation.Block == (int)block)
        .Sum(allocation => allocation.QuantityKg);

    /// <summary>The one free-kg rule of every consumer (an over-reserved stored state leaves nothing unreserved).</summary>
    private static decimal Unreserved(decimal freeKg, decimal allocatedKg)
    {
        try
        {
            return StockAllocation.Unreserved(Kilograms.Of(freeKg), Kilograms.Of(allocatedKg)).Value;
        }
        catch (InventoryRuleException)
        {
            return 0m;
        }
    }

    private sealed class Totals
    {
        public decimal ShippedKg { get; set; }
        public decimal CustomerReturnedKg { get; set; }
        public decimal ManufacturingIssuedKg { get; set; }
        public decimal ManufacturingReturnedKg { get; set; }
        public decimal TransferDispatchedKg { get; set; }
        public decimal TransferAcceptedKg { get; set; }
        public decimal ScrappedKg { get; set; }
        public decimal CountAdjustedInKg { get; set; }
        public decimal CountAdjustedOutKg { get; set; }
        public decimal OtherInKg { get; set; }
        public decimal OtherOutKg { get; set; }
    }
}

/// <summary>One physical place of a label: owner, custodian and location, without the block state.</summary>
internal readonly record struct DispositionPositionKey(int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId,
    Guid LocationId);

internal sealed record DispositionLineFact(Guid MovementId, int Kind, string SourceKind, Guid SourceId, Guid? SourceLineId,
    int Direction, int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId, Guid LocationId, Guid ProductId,
    int Block, decimal QuantityKg)
{
    public DispositionPositionKey Key => new(OwnerKind, OwnerId, CustodianKind, CustodianId, LocationId);
}

/// <summary>A balance bucket, or an active allocation's remaining kg on its position.</summary>
internal sealed record DispositionBalanceFact(int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId, Guid LocationId,
    Guid ProductId, int Block, decimal QuantityKg)
{
    public DispositionPositionKey Key => new(OwnerKind, OwnerId, CustodianKind, CustodianId, LocationId);
}

/// <summary>
/// The output a label was received from, with the figures at the output's original position: Free kg, the active
/// allocations on it, the free unallocated rest (the only reversible kg) and the blocked kg there.
/// </summary>
internal sealed record InventoryLabelOutputSnapshot(Guid ManufacturingOrderId, Guid OutputId, Guid ReceiptMovementId,
    DispositionPositionKey Position, decimal ProducedKg, decimal ReversedKg, decimal FreeAtPositionKg,
    decimal AllocatedAtPositionKg, decimal FreeUnallocatedAtPositionKg, decimal BlockedAtPositionKg);

internal sealed record InventoryLabelDispositionSnapshot(
    InventoryLabelDispositionStatusV1 Status,
    Guid LabelId,
    Guid ProductId,
    long LabelVersion,
    InventoryLabelOutputSnapshot? Output,
    decimal ShippedKg,
    decimal CustomerReturnedKg,
    decimal ManufacturingIssuedKg,
    decimal ManufacturingReturnedKg,
    decimal TransferDispatchedKg,
    decimal TransferAcceptedKg,
    decimal ScrappedKg,
    decimal CountAdjustedInKg,
    decimal CountAdjustedOutKg,
    decimal OtherInKg,
    decimal OtherOutKg,
    decimal PhysicalKg,
    decimal BlockedKg,
    decimal AllocatedKg,
    decimal FreeUnallocatedKg,
    int FreePositionCount)
{
    internal static InventoryLabelDispositionSnapshot NotFound { get; } =
        Empty(InventoryLabelDispositionStatusV1.NotFound);

    internal static InventoryLabelDispositionSnapshot Unreadable { get; } =
        Empty(InventoryLabelDispositionStatusV1.Unreadable);

    private static InventoryLabelDispositionSnapshot Empty(InventoryLabelDispositionStatusV1 status) =>
        new(status, Guid.Empty, Guid.Empty, 0L, null, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0);
}
