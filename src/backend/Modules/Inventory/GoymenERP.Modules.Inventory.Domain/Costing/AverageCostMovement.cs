namespace GoymenERP.Modules.Inventory.Domain.Costing;

public enum AverageCostMovementKind
{
    /// <summary>A purchase receipt: the only input that recalculates the average (SAT-D257).</summary>
    PurchaseReceipt = 1,

    /// <summary>Stock leaves (sale, consumption): the average stays, the quantity falls.</summary>
    Issue = 2,

    /// <summary>A production receipt: never part of the purchase average (SAT-D258).</summary>
    ProductionReceipt = 3,

    /// <summary>
    /// A return the core cannot classify (neither <see cref="CustomerReturn"/> nor <see cref="PurchaseReturn"/>): it stays
    /// unreadable (<see cref="AverageCostUnreadableReason.Return"/>), nothing is guessed.
    /// </summary>
    Return = 4,

    /// <summary>
    /// SAT-D303: a customer return enters as the reversal of the original issue, at the original issue's unit cost
    /// (<see cref="AverageCostMovement.UnitCostPerStockKg"/>; KLN-D64), and blends like a receipt. Without that cost the
    /// position is unreadable (<see cref="AverageCostUnreadableReason.Return"/>).
    /// </summary>
    CustomerReturn = 5,

    /// <summary>
    /// SAT-D306: a purchase return leaves at the original purchase line's base-currency unit cost
    /// (<see cref="AverageCostMovement.UnitCostPerStockKg"/>), not at the current average, and the remaining average is
    /// recalculated. Without that cost the position is unreadable (<see cref="AverageCostUnreadableReason.Return"/>).
    /// </summary>
    PurchaseReturn = 6,
}

/// <summary>
/// One physical movement on a position, as the cost core sees it. <see cref="StockOnHandBeforeKg"/> is Inventory's own
/// physical stock of the position before the movement; the core refuses to blend a quantity the ledger never costed.
/// </summary>
/// <param name="UnitCostPerStockKg">The base-currency cost of one stock kilogram: the purchase price for a purchase receipt
/// (required), the original issue's unit cost for a customer return and the original purchase line's unit cost for a
/// purchase return (null when that original cost is unavailable). Ignored for an issue or a production receipt.</param>
/// <param name="UnitCostIsPerStockKg">False when the purchase price is not per stock kilogram (unit conversion is OPEN).</param>
/// <param name="IsBaseCurrency">False for a foreign-currency cost (rate and date are OPEN).</param>
/// <param name="AdditionalCostPerStockKg">Purchase receipts only: freight, customs or other additional cost per stock kilogram,
/// kept apart from <paramref name="UnitCostPerStockKg"/>. SAT-D302 (1): it never enters the v1 average. Keeping it apart in
/// its own field is the implementation reading of that decision (reversible); it is carried so the later setting
/// (SAT-D302 (2), ALS-D80, owner and default with the expense structure) has its input. Non-negative when given.</param>
public sealed record AverageCostMovement(
    AverageCostMovementKind Kind,
    decimal QuantityKg,
    decimal StockOnHandBeforeKg,
    decimal? UnitCostPerStockKg = null,
    bool UnitCostIsPerStockKg = true,
    bool IsBaseCurrency = true,
    decimal? AdditionalCostPerStockKg = null);
