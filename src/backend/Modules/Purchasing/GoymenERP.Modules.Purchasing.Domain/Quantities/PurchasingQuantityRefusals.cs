using GoymenERP.BuildingBlocks.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Quantities;

/// <summary>
/// Purchasing's refusal codes for the shared quantity rule (TEK-02): the arithmetic lives once in
/// <see cref="Quantity{TRefusals}"/>; Purchasing keeps its stable codes (<c>purchasing_unit_mismatch</c>,
/// <c>purchasing_quantity_below_zero</c>) and its own quantity type. Within Purchasing the shared type is
/// spelled <c>Quantity</c> through the project's <c>global using</c> alias.
/// </summary>
public sealed class PurchasingQuantityRefusals : IQuantityRuleRefusals
{
    private PurchasingQuantityRefusals()
    {
    }

    public static Exception UnitMismatch(string message) => new PurchasingRuleException(PurchasingRuleCodes.UnitMismatch, message);

    public static Exception BelowZero(string message) => new PurchasingRuleException(PurchasingRuleCodes.QuantityBelowZero, message);
}
