namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>The two positions a transfer leaves behind.</summary>
public sealed record AverageCostTransferResult(AverageCostPosition Source, AverageCostPosition Target);

/// <summary>
/// The deterministic moving-average purchase cost core (SAT-D256…SAT-D258, SAT-D262, SAT-D265). One calculator, no screen
/// or caller re-implements it. Pure: no clock, no storage, no company or role. The key of a position (company, location,
/// product) lives in the ledger; this core only computes the next state of one position.
/// <list type="bullet">
/// <item>A purchase receipt recalculates: <c>round4((q·a + Δ·c) / (q + Δ))</c>, rounded once, AwayFromZero.</item>
/// <item>An issue keeps the average and lowers the quantity; below zero is unreadable, never negative.</item>
/// <item>A transfer enters the target at the SOURCE average (the target's own average then blends as a receipt).</item>
/// <item>A production receipt never enters the average: the position becomes unreadable (mixed source is OPEN).</item>
/// <item>Negative stock, unit conversion, foreign currency, landed cost and returns are NOT decided: they answer
/// <see cref="AverageCostStatus.Unreadable"/> with their <see cref="AverageCostUnreadableReason"/>; nothing is guessed.</item>
/// </list>
/// A position never seen by the ledger is <c>null</c> (it reads as NoSource). OPEN-INV-AVGCOST-ZERO-STOCK (NEEDS-SINAN):
/// a position that falls to exactly zero keeps its last average; whether that is a readable cost is not decided.
/// </summary>
public static class AverageCostCalculator
{
    public static AverageCostPosition Apply(AverageCostPosition? current, AverageCostMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (!Enum.IsDefined(movement.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "Unknown movement kind.");
        }

        if (movement.QuantityKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "A movement moves a positive quantity.");
        }

        if (movement.StockOnHandBeforeKg < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "Physical stock before the movement is not negative.");
        }

        if (movement.Kind == AverageCostMovementKind.PurchaseReceipt
            && (movement.UnitCostPerStockKg is null or < 0m))
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "A purchase receipt carries a non-negative unit cost.");
        }

        if (current is { Status: AverageCostStatus.Unreadable })
        {
            return current;
        }

        var trackedKg = current?.QuantityKg ?? 0m;
        if (movement.StockOnHandBeforeKg != trackedKg)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.LedgerStockMismatch);
        }

        return movement.Kind switch
        {
            AverageCostMovementKind.PurchaseReceipt => ApplyReceipt(current, movement),
            AverageCostMovementKind.Issue => ApplyIssue(current, movement),
            AverageCostMovementKind.ProductionReceipt =>
                AverageCostPosition.Unreadable(AverageCostUnreadableReason.ProductionSource),
            _ => AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return),
        };
    }

    /// <summary>
    /// Moves <paramref name="quantityKg"/> from source to target. The target receives it at the source's average. Either
    /// position is unreadable when its own side cannot be computed; an unreadable or unknown source makes the target
    /// unreadable (it cannot be costed from nothing).
    /// </summary>
    public static AverageCostTransferResult ApplyTransfer(
        AverageCostPosition? source,
        decimal sourceStockOnHandBeforeKg,
        AverageCostPosition? target,
        decimal targetStockOnHandBeforeKg,
        decimal quantityKg)
    {
        var sourceAfter = Apply(source, new AverageCostMovement(
            AverageCostMovementKind.Issue, quantityKg, sourceStockOnHandBeforeKg));
        if (source is not { Status: AverageCostStatus.Known } || sourceAfter.Status != AverageCostStatus.Known)
        {
            // The target is not touched by a source that has no readable average to hand over.
            var targetUnreadable = target is { Status: AverageCostStatus.Unreadable }
                ? target
                : AverageCostPosition.Unreadable(AverageCostUnreadableReason.TransferSourceUnreadable);
            return new(sourceAfter, targetUnreadable);
        }

        var targetAfter = Apply(target, new AverageCostMovement(
            AverageCostMovementKind.PurchaseReceipt, quantityKg, targetStockOnHandBeforeKg, source.AverageCostPerKg));
        return new(sourceAfter, targetAfter);
    }

    private static AverageCostPosition ApplyReceipt(AverageCostPosition? current, AverageCostMovement movement)
    {
        if (!movement.UnitCostIsPerStockKg)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.UnitConversion);
        }

        if (!movement.IsBaseCurrency)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.ForeignCurrency);
        }

        if (movement.IncludesAdditionalCost)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.LandedOrAdditionalCost);
        }

        var receiptCost = movement.UnitCostPerStockKg!.Value;
        var heldKg = current?.QuantityKg ?? 0m;
        var newKg = heldKg + movement.QuantityKg;
        if (heldKg == 0m)
        {
            return AverageCostPosition.Known(newKg, AverageCostPrecision.Round(receiptCost));
        }

        var heldCost = current!.AverageCostPerKg!.Value;
        var blended = ((heldKg * heldCost) + (movement.QuantityKg * receiptCost)) / newKg;
        return AverageCostPosition.Known(newKg, AverageCostPrecision.Round(blended));
    }

    private static AverageCostPosition ApplyIssue(AverageCostPosition? current, AverageCostMovement movement)
    {
        var heldKg = current?.QuantityKg ?? 0m;
        if (current is null || movement.QuantityKg > heldKg)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.NegativeStock);
        }

        return AverageCostPosition.Known(heldKg - movement.QuantityKg, current.AverageCostPerKg!.Value);
    }
}
