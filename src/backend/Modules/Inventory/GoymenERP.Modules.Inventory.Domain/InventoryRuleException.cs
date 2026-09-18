namespace GoymenERP.Modules.Inventory.Domain;

/// <summary>
/// A stock rule refused the operation. <see cref="Code"/> is the stable, machine-readable
/// reason; the message is for people. Nothing is partly applied when this is thrown.
/// </summary>
public sealed class InventoryRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The stable refusal codes. A consumer maps these, never the message text.</summary>
public static class InventoryRuleCodes
{
    /// <summary>A kilogram value needs more than 3 decimals (Ö2); it is refused, never cut.</summary>
    public const string KilogramPrecisionExceeded = "inventory_kg_precision_exceeded";

    /// <summary>A movement quantity must be a positive kilogram amount.</summary>
    public const string QuantityNotPositive = "inventory_quantity_not_positive";

    /// <summary>The movement would take a physical balance below zero.</summary>
    public const string NegativePhysicalStock = "inventory_negative_physical_stock";

    /// <summary>The same movement was offered to the ledger (or to a reconciliation) a second time.</summary>
    public const string DuplicateMovement = "inventory_duplicate_movement";

    /// <summary>
    /// One label would hold stock in two physical places (company, owner, custodian, location
    /// or product) at once. A label is one physical piece: moving it is a transfer (T13), a
    /// second piece is a split with a new identity (T13). Only its block state may differ.
    /// </summary>
    public const string LabelInTwoPositions = "inventory_label_in_two_positions";

    /// <summary>A required identity (company, owner, location, product, lot, label, …) is missing.</summary>
    public const string MissingReference = "inventory_missing_reference";

    /// <summary>The movement's lines do not describe a valid movement of its kind.</summary>
    public const string InvalidMovement = "inventory_invalid_movement";

    /// <summary>A cost result contradicts its own status (e.g. an amount while not calculated).</summary>
    public const string InvalidCostResult = "inventory_invalid_cost_result";

    /// <summary>A label document requirement or reprint request is not valid.</summary>
    public const string InvalidLabelOperation = "inventory_invalid_label_operation";

    /// <summary>
    /// A position names a label of another company. A label is a tenant record of the company
    /// that holds its stock (ADR-INV-001 A); it never appears under another company.
    /// </summary>
    public const string LabelCompanyMismatch = "inventory_label_company_mismatch";

    /// <summary>
    /// A receipt names a label that already holds stock. A label is one physical piece that
    /// arrives once; a second arrival of a stock-bearing label is a double count (ORG #281 P3-1).
    /// A label whose stock has all left may be received again (a return is T13's own kind).
    /// </summary>
    public const string LabelAlreadyInStock = "inventory_label_already_in_stock";
}
