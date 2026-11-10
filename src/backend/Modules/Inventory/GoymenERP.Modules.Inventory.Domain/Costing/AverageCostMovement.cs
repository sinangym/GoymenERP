namespace GoymenERP.Modules.Inventory.Domain.Costing;

public enum AverageCostMovementKind
{
    /// <summary>A purchase receipt: the only input that recalculates the average (SAT-D257).</summary>
    PurchaseReceipt = 1,

    /// <summary>Stock leaves (sale, consumption): the average stays, the quantity falls.</summary>
    Issue = 2,

    /// <summary>A production receipt: never part of the purchase average (SAT-D258).</summary>
    ProductionReceipt = 3,

    /// <summary>OPEN-INV-AVGCOST-RETURN (NEEDS-SINAN): a sales or purchase return.</summary>
    Return = 4,
}

/// <summary>
/// One physical movement on a position, as the cost core sees it. <see cref="StockOnHandBeforeKg"/> is Inventory's own
/// physical stock of the position before the movement; the core refuses to blend a quantity the ledger never costed.
/// </summary>
/// <param name="UnitCostPerStockKg">Purchase receipts only: the base-currency cost of one stock kilogram.</param>
/// <param name="UnitCostIsPerStockKg">False when the purchase price is not per stock kilogram (unit conversion is OPEN).</param>
/// <param name="IsBaseCurrency">False for a foreign-currency cost (rate and date are OPEN).</param>
/// <param name="IncludesAdditionalCost">True when freight, customs or other additional cost is mixed in (landed is OPEN).</param>
public sealed record AverageCostMovement(
    AverageCostMovementKind Kind,
    decimal QuantityKg,
    decimal StockOnHandBeforeKg,
    decimal? UnitCostPerStockKg = null,
    bool UnitCostIsPerStockKg = true,
    bool IsBaseCurrency = true,
    bool IncludesAdditionalCost = false);
