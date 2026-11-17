using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Canonical coprime integer strings, not rounded decimal approximations or a second money profile.</summary>
public sealed record InventoryExactCostRatioV1(string Numerator, string Denominator)
{
    public static InventoryExactCostRatioV1 From(ExactCostRatio value) => new(value.Numerator, value.Denominator);
    public ExactCostRatio Value() => ExactCostRatio.TryParse(Numerator, Denominator, out var value)
        ? value! : throw new InvalidOperationException("The recorded exact cost ratio is not canonical.");

    /// <summary>Legacy scalar-only history remains valid. A supplied ratio is authoritative only when any supplied
    /// scalar denotes the identical exact value; absent or contradictory evidence is never rounded into agreement.</summary>
    public static ExactCostRatio Resolve(decimal? scalar, InventoryExactCostRatioV1? ratio)
    {
        var exact = ratio?.Value() ?? (scalar is { } value ? ExactCostRatio.Of(value)
            : throw new InvalidOperationException("The recorded exact value is missing."));
        if (scalar is { } actual && exact.CompareTo(ExactCostRatio.Of(actual)) != 0)
            throw new InvalidOperationException("The recorded scalar contradicts its exact ratio.");
        return exact;
    }

    public static decimal? ScalarOf(ExactCostRatio value) => value.TryToDecimal(out var scalar) ? scalar : null;
}
