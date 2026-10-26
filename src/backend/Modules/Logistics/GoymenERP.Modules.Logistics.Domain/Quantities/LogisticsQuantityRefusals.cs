using GoymenERP.BuildingBlocks.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Quantities;

/// <summary>
/// Logistics's refusal codes for the shared quantity rule (TEK-02): the arithmetic lives once in
/// <see cref="Quantity{TRefusals}"/>; Logistics keeps its stable codes (<c>logistics_unit_mismatch</c>,
/// <c>logistics_quantity_below_zero</c>) and its own quantity type. Within Logistics the shared type is
/// spelled <c>Quantity</c> through the project's <c>global using</c> alias.
/// </summary>
public sealed class LogisticsQuantityRefusals : IQuantityRuleRefusals
{
    private LogisticsQuantityRefusals()
    {
    }

    public static Exception UnitMismatch(string message) => new LogisticsRuleException(LogisticsRuleCodes.UnitMismatch, message);

    public static Exception BelowZero(string message) => new LogisticsRuleException(LogisticsRuleCodes.QuantityBelowZero, message);
}
