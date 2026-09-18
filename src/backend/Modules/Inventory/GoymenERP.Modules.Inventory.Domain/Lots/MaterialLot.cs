namespace GoymenERP.Modules.Inventory.Domain.Lots;

/// <summary>
/// The minimal lot record a label belongs to (DEC-PRD-003: the label is the real physical
/// package/lot record's; Ö3: lot/label trace is the mandatory minimum). One lot, one company, one
/// product.
/// <list type="bullet">
/// <item>The company never changes: a lot is the stock-holding company's (ADR-INV-001 A), as its
/// labels are.</item>
/// <item>Heat and coil are recorded only when real (Ö3); nothing here generates or defaults them,
/// and an unknown one stays null.</item>
/// <item>Deliberately not here: origin (its dictionary is an open DATA item), MTC or certificates
/// (T30), quality or release state (Ö3: nothing is released by this record), genealogy (T13), a
/// display code (no format decision) and cost (OPEN-008).</item>
/// </list>
/// </summary>
public sealed class MaterialLot
{
    /// <summary>The longest heat or coil number this module stores (the label's own limit).</summary>
    public const int MaximumTraceLength = 64;

    public MaterialLot(Guid id, Guid companyId, Guid productId, string? heatNumber = null, string? coilNumber = null)
    {
        Id = RequireId(id, "Lot");
        CompanyId = RequireId(companyId, "Lotun şirketi");
        ProductId = RequireId(productId, "Ürün");
        HeatNumber = Optional(heatNumber);
        CoilNumber = Optional(coilNumber);
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>The product this lot is of, by identity only (Product owns the card).</summary>
    public Guid ProductId { get; }

    /// <summary>Only when a real heat number is known; never generated.</summary>
    public string? HeatNumber { get; }

    /// <summary>Only when a real coil number is known; never generated.</summary>
    public string? CoilNumber { get; }

    private static Guid RequireId(Guid id, string what) =>
        id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, $"{what} kimliği zorunludur.")
            : id;

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
