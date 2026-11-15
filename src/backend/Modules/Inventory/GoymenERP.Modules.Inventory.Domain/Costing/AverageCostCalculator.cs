namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>The two positions a transfer leaves behind.</summary>
public sealed record AverageCostTransferResult(AverageCostPosition Source, AverageCostPosition Target);

/// <summary>The two positions a transfer dispatch cancellation leaves behind (KLN-D64).</summary>
public sealed record AverageCostTransferDispatchCancelResult(AverageCostPosition DispatchedTo, AverageCostPosition Source);

/// <summary>
/// The deterministic moving-average purchase cost core (SAT-D256…SAT-D258, SAT-D262, SAT-D265). One calculator, no screen
/// or caller re-implements it. Pure: no clock, no storage, no company or role. The key of a position (company, location,
/// product) lives in the ledger; this core only computes the next state of one position.
/// <list type="bullet">
/// <item>A purchase receipt with a positive cost recalculates: <c>round4((q·a + Δ·c) / (q + Δ))</c>, rounded once, AwayFromZero.</item>
/// <item>An issue keeps the average and lowers the quantity; below zero is unreadable, never negative.</item>
/// <item>A transfer enters the target at the SOURCE average (the target's own average then blends as a receipt).</item>
/// <item>A cancelled transfer dispatch returns to the source at the unit cost used at dispatch, never at the source's
/// average at cancel time (KLN-D64 (1)).</item>
/// <item>A production receipt never enters the average: the position becomes unreadable (mixed source is OPEN).</item>
/// <item>Additional (landed) cost never enters the average in v1 (SAT-D302 (1)). The receipt entering at its own purchase
/// price, with <see cref="AverageCostMovement.AdditionalCostPerStockKg"/> kept outside the figure, is the implementation
/// reading of that decision (reversible).</item>
/// <item>Stock never goes below zero (SAT-D318 (1)) and no recovery-from-negative formula is written (SAT-D323 (2)): an
/// issue that would breach this answers <see cref="AverageCostUnreadableReason.NegativeStock"/>, fail-closed (the decision
/// file's author reading; see that reason). Unit conversion and foreign currency are decided (SAT-D298, BS-1) but not
/// implemented in this core yet: they answer <see cref="AverageCostStatus.Unreadable"/> with their
/// <see cref="AverageCostUnreadableReason"/>; nothing is guessed.</item>
/// <item>SAT-D303: a customer return enters at the original issue's unit cost and blends like a receipt.</item>
/// <item>SAT-D306: a purchase return leaves at the original purchase line's unit cost:
/// <c>round4((q·a − Δ·c) / (q − Δ))</c>. A residual value at zero stock, or a non-positive remaining value, would need a
/// cost-difference record whose posting is not specified: the position is unreadable
/// (<see cref="AverageCostUnreadableReason.Return"/>), fail-closed, never a negative or invented average.</item>
/// <item>SAT-D304: an approved opening cost seeds a position the ledger never tracked (<see cref="ApplyOpeningCost"/>).</item>
/// </list>
/// A position never seen by the ledger is <c>null</c> (it reads as NoSource). A position that falls to exactly zero keeps
/// its last average as a readable value (Sinan 2026-11-11). A zero unit cost is "cost unknown": the receipt makes the
/// position <see cref="AverageCostStatus.Unreadable"/> (<see cref="AverageCostUnreadableReason.ZeroCostReceipt"/>).
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

        if (movement.Kind is AverageCostMovementKind.CustomerReturn or AverageCostMovementKind.PurchaseReturn
            && movement.UnitCostPerStockKg is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "A return's original unit cost is not negative.");
        }

        if (movement.AdditionalCostPerStockKg is { } additional
            && (movement.Kind != AverageCostMovementKind.PurchaseReceipt || additional < 0m))
        {
            throw new ArgumentOutOfRangeException(
                nameof(movement), "Only a purchase receipt carries an additional cost, and it is non-negative.");
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
            AverageCostMovementKind.CustomerReturn => ApplyCustomerReturn(current, movement),
            AverageCostMovementKind.PurchaseReturn => ApplyPurchaseReturn(current, movement),
            _ => AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return),
        };
    }

    /// <summary>
    /// SAT-D304: the approved opening cost of a position the ledger never tracked (<paramref name="current"/> is null).
    /// It costs the position's whole physical stock on hand at the entered base-currency unit cost per stock kilogram.
    /// A position that already has a ledger state is never overwritten (the command refuses before calling); a missing
    /// cost is simply not entered ("yok", never zero), so a zero cost is refused here.
    /// </summary>
    public static AverageCostPosition ApplyOpeningCost(
        AverageCostPosition? current, decimal stockOnHandKg, decimal unitCostPerStockKg)
    {
        if (current is not null)
        {
            throw new InvalidOperationException("An opening cost seeds only a position the ledger never tracked.");
        }

        if (stockOnHandKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(stockOnHandKg), "An opening cost costs positive stock on hand.");
        }

        if (unitCostPerStockKg <= 0m || !AverageCostPrecision.IsAtInternalScale(unitCostPerStockKg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitCostPerStockKg), "An opening cost is positive with at most the internal scale of decimals.");
        }

        return AverageCostPosition.Known(stockOnHandKg, unitCostPerStockKg);
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

    /// <summary>
    /// KLN-D64 (1): the reverse movement of a cancelled transfer dispatch. <paramref name="quantityKg"/> leaves the position
    /// the dispatch moved it to (an issue at that position's average) and re-enters the source at
    /// <paramref name="dispatchUnitCostPerKg"/>, the unit cost the dispatch used. The source's own average at cancel time
    /// is never used for the returning quantity; it only blends with it as a receipt. A dispatch whose unit cost is unknown
    /// (<c>null</c>) leaves the source unreadable (<see cref="AverageCostUnreadableReason.TransferSourceUnreadable"/>):
    /// the returning quantity cannot be costed from nothing, and the current average is not a stand-in. A zero dispatch
    /// cost is "cost unknown" like any receipt (<see cref="AverageCostUnreadableReason.ZeroCostReceipt"/>).
    /// KLN-D64 (2): while transfers do not post to the cost ledger, the cancellation does not either; this rule is ready
    /// for when transfer cost posting starts and no movement path calls it before then.
    /// </summary>
    public static AverageCostTransferDispatchCancelResult ApplyTransferDispatchCancel(
        AverageCostPosition? dispatchedTo,
        decimal dispatchedToStockOnHandBeforeKg,
        AverageCostPosition? source,
        decimal sourceStockOnHandBeforeKg,
        decimal quantityKg,
        decimal? dispatchUnitCostPerKg)
    {
        if (dispatchUnitCostPerKg is < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dispatchUnitCostPerKg), "A dispatch unit cost, when known, is not negative.");
        }

        var dispatchedToAfter = Apply(dispatchedTo, new AverageCostMovement(
            AverageCostMovementKind.Issue, quantityKg, dispatchedToStockOnHandBeforeKg));
        if (dispatchUnitCostPerKg is not { } dispatchUnitCost)
        {
            // The source is not re-costed from its own average: without the dispatch cost it has no figure to take back.
            var sourceUnreadable = source is { Status: AverageCostStatus.Unreadable }
                ? source
                : AverageCostPosition.Unreadable(AverageCostUnreadableReason.TransferSourceUnreadable);
            return new(dispatchedToAfter, sourceUnreadable);
        }

        var sourceAfter = Apply(source, new AverageCostMovement(
            AverageCostMovementKind.PurchaseReceipt, quantityKg, sourceStockOnHandBeforeKg, dispatchUnitCost));
        return new(dispatchedToAfter, sourceAfter);
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

        // SAT-D302 (1): additional cost (freight, customs) never enters the v1 average. The receipt entering at its own
        // purchase price is the implementation reading of that decision (reversible). Turning it on later is a setting
        // whose key, default and owner come with the expense structure (SAT-D302 (2), ALS-D80).
        var receiptCost = movement.UnitCostPerStockKg!.Value;
        if (receiptCost == 0m)
        {
            // Sinan 2026-11-11: a zero unit cost is "cost unknown", it never enters the average.
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.ZeroCostReceipt);
        }

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

    /// <summary>
    /// SAT-D303 / KLN-D64: the reversal of the original issue enters at that issue's unit cost and blends like a
    /// receipt. Without the original cost the position is unreadable; nothing is guessed from the current average.
    /// </summary>
    private static AverageCostPosition ApplyCustomerReturn(AverageCostPosition? current, AverageCostMovement movement)
    {
        if (movement.UnitCostPerStockKg is null)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return);
        }

        return ApplyReceipt(current, movement);
    }

    /// <summary>
    /// SAT-D306: the return leaves at the original purchase line's unit cost <c>c</c>; the remaining average is
    /// <c>round4((q·a − Δ·c) / (q − Δ))</c>. Returning more than is held is negative stock (SAT-D301/D318). A return that
    /// empties the position keeps its last average only when no value is left over; a left-over value, or a remaining
    /// value that is not positive, needs a cost-difference record whose posting is not specified (SAT-D306 (c)): the
    /// position is unreadable instead, fail-closed.
    /// </summary>
    private static AverageCostPosition ApplyPurchaseReturn(AverageCostPosition? current, AverageCostMovement movement)
    {
        var heldKg = current?.QuantityKg ?? 0m;
        if (current is null || movement.QuantityKg > heldKg)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.NegativeStock);
        }

        if (movement.UnitCostPerStockKg is not { } returnCost)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return);
        }

        if (!movement.UnitCostIsPerStockKg)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.UnitConversion);
        }

        if (!movement.IsBaseCurrency)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.ForeignCurrency);
        }

        if (returnCost == 0m)
        {
            // A zero cost is "cost unknown" (Sinan 2026-11-11), on the way out as on the way in.
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.ZeroCostReceipt);
        }

        var heldCost = current.AverageCostPerKg!.Value;
        var remainingKg = heldKg - movement.QuantityKg;
        var remainingValue = (heldKg * heldCost) - (movement.QuantityKg * returnCost);
        if (remainingKg == 0m)
        {
            return remainingValue == 0m
                ? AverageCostPosition.Known(0m, heldCost)
                : AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return);
        }

        if (remainingValue <= 0m)
        {
            return AverageCostPosition.Unreadable(AverageCostUnreadableReason.Return);
        }

        return AverageCostPosition.Known(remainingKg, AverageCostPrecision.Round(remainingValue / remainingKg));
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
