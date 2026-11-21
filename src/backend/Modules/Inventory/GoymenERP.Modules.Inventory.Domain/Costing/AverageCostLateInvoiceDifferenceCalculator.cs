namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>Whether a late supplier-invoice cost difference joined the current average.</summary>
public enum AverageCostLateInvoiceDifferenceStatus
{
    /// <summary>The difference joined the current TL average once (or was zero and changed nothing).</summary>
    Absorbed = 1,

    /// <summary>The position is unreadable; the sticky state is kept and nothing is guessed.</summary>
    PositionUnreadable = 2,

    /// <summary>
    /// The position holds no stock, so there is no current average to join. SAT-D413 sends this unabsorbed difference
    /// to a separate SMM proposal; this legacy result alone does not post or invent an account.
    /// </summary>
    NoStockToAbsorb = 3,

    /// <summary>
    /// A credit difference larger than the position's whole value would leave a negative average; never written
    /// (fail-closed, the same rule as SAT-D306 (c)).
    /// </summary>
    DifferenceExceedsValue = 4,
}

/// <summary>One answer of <see cref="AverageCostLateInvoiceDifferenceCalculator"/>.</summary>
/// <param name="Status">What happened to the difference.</param>
/// <param name="Position">The position after the step; the unchanged position whenever the difference did not join.</param>
/// <param name="DifferenceValue">The exact TL difference value, <c>receivedKg · (invoiceCost − receiptCost)</c>, unrounded.</param>
public sealed record AverageCostLateInvoiceDifferenceResult(
    AverageCostLateInvoiceDifferenceStatus Status,
    AverageCostPosition Position,
    decimal DifferenceValue);

/// <summary>SAT-D385/D404/D413: an exact, already known receipt-cost difference. The source amount is split without
/// rounding; the current average alone uses SAT-D265's existing 4dp rounding. Its actual pool effect and rounding
/// difference remain distinct evidence, not another SMM or tax policy.</summary>
public sealed record ExactAverageCostLateInvoiceDifferenceResult(
    ExactAverageCostPool Position,
    ExactCostRatio SourceDifference,
    ExactCostRatio AbsorbedDifference,
    ExactCostRatio UnabsorbedDifference,
    ExactCostRatio RecordedPoolValueEffect,
    ExactCostRatio AverageRoundingDifference);

/// <summary>
/// SAT-D385 / SAT-D404: when the supplier invoice of a foreign-currency receipt arrives late and its official TL cost differs
/// from the TL cost the receipt entered with, the difference joins the CURRENT TL average once, as a difference movement
/// dated at the invoice: <c>new = round4((heldKg · heldCost + receivedKg · (invoiceCost − receiptCost)) / heldKg)</c>.
/// History is never resequenced, the stock quantity does not change, and nothing is spread back over the issues that
/// happened in between. BS-1 (SAT-D295) is unchanged: a receipt with no TL cost (invoice not recorded) never entered the
/// average, so it has no difference to absorb; its cost enters as a receipt when the invoice arrives. Deterministic: the
/// exact difference is carried and rounded once, at the recalculated average (SAT-D265).
/// </summary>
public static class AverageCostLateInvoiceDifferenceCalculator
{
    /// <summary>For a proven current priced pool and a proven invoice-minus-original-receipt amount only.
    /// Missing/first-recognition cost is not zero and must never be passed to this difference path.</summary>
    public static ExactAverageCostLateInvoiceDifferenceResult ApplyExact(
        ExactAverageCostPool current, ExactCostRatio sourceDifference)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sourceDifference);
        if (current.QuantityKg.Sign < 0 || current.AverageCostPerKg < 0m
            || !AverageCostPrecision.IsAtInternalScale(current.AverageCostPerKg))
            throw new ArgumentException("The current priced pool must be readable at its recorded precision.", nameof(current));

        var zero = ExactCostRatio.Of(0m);
        var absorbed = current.QuantityKg.Sign == 0 ? zero
            : current.Value.Add(sourceDifference).Sign < 0 ? current.Value.Multiply(-1m) : sourceDifference;
        var unabsorbed = sourceDifference.Subtract(absorbed);
        var after = absorbed.Sign == 0 ? current : new ExactAverageCostPool(current.QuantityKg,
            current.Value.Add(absorbed).Divide(current.QuantityKg).Round(AverageCostPrecision.InternalScale));
        var actualEffect = after.Value.Subtract(current.Value);
        return new(after, sourceDifference, absorbed, unabsorbed, actualEffect, actualEffect.Subtract(absorbed));
    }

    public static AverageCostLateInvoiceDifferenceResult Apply(
        AverageCostPosition current,
        decimal receivedKg,
        decimal receiptUnitCostPerStockKg,
        decimal invoiceUnitCostPerStockKg)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (receivedKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedKg), "A received quantity is positive.");
        }

        if (receiptUnitCostPerStockKg <= 0m || invoiceUnitCostPerStockKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(invoiceUnitCostPerStockKg), "Known receipt and invoice costs are positive; an unknown cost is not passed in.");
        }

        var difference = receivedKg * (invoiceUnitCostPerStockKg - receiptUnitCostPerStockKg);
        if (current.Status != AverageCostStatus.Known
            || current.QuantityKg is not { } heldKg
            || current.AverageCostPerKg is not { } heldCost)
        {
            return new(AverageCostLateInvoiceDifferenceStatus.PositionUnreadable, current, difference);
        }

        if (difference == 0m)
        {
            return new(AverageCostLateInvoiceDifferenceStatus.Absorbed, current, difference);
        }

        if (heldKg == 0m)
        {
            return new(AverageCostLateInvoiceDifferenceStatus.NoStockToAbsorb, current, difference);
        }

        var newValue = (heldKg * heldCost) + difference;
        if (newValue < 0m)
        {
            return new(AverageCostLateInvoiceDifferenceStatus.DifferenceExceedsValue, current, difference);
        }

        return new(AverageCostLateInvoiceDifferenceStatus.Absorbed,
            AverageCostPosition.Known(heldKg, AverageCostPrecision.Round(newValue / heldKg)), difference);
    }
}
