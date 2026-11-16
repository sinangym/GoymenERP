namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>Whether a late supplier-invoice cost difference joined the current average.</summary>
public enum AverageCostLateInvoiceDifferenceStatus
{
    /// <summary>The difference joined the current TL average once (or was zero and changed nothing).</summary>
    Absorbed = 1,

    /// <summary>The position is unreadable; the sticky state is kept and nothing is guessed.</summary>
    PositionUnreadable = 2,

    /// <summary>
    /// The position holds no stock, so there is no current average to join. Where such a difference goes is not decided
    /// (fail-closed; the caller keeps the difference unposted and surfaces it).
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
