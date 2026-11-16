using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Canonical coprime integer strings, not rounded decimal approximations or a second money profile.</summary>
public sealed record InventoryExactCostRatioV1(string Numerator, string Denominator)
{
    public static InventoryExactCostRatioV1 From(ExactCostRatio value) => new(value.Numerator, value.Denominator);
    public ExactCostRatio Value() => ExactCostRatio.TryParse(Numerator, Denominator, out var value)
        ? value! : throw new InvalidOperationException("The recorded exact cost ratio is not canonical.");
}
