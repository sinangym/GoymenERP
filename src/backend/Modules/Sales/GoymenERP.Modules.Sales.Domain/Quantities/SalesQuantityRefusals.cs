using GoymenERP.BuildingBlocks.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Quantities;

/// <summary>
/// Sales's refusal codes for the shared quantity rule (TEK-02): the arithmetic lives once in
/// <see cref="Quantity{TRefusals}"/>; Sales keeps its stable codes (<c>sales_unit_mismatch</c>,
/// <c>sales_quantity_below_zero</c>) and its own quantity type. Within Sales the shared type is
/// spelled <c>Quantity</c> through the project's <c>global using</c> alias.
/// </summary>
public sealed class SalesQuantityRefusals : IQuantityRuleRefusals
{
    private SalesQuantityRefusals()
    {
    }

    public static Exception UnitMismatch(string message) => new SalesRuleException(SalesRuleCodes.UnitMismatch, message);

    public static Exception BelowZero(string message) => new SalesRuleException(SalesRuleCodes.QuantityBelowZero, message);
}
