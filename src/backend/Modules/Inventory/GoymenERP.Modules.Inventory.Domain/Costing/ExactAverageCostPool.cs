namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>Private priced share of a physical position. Repacking can make the priced kg a non-terminating rational;
/// only the existing final 4dp average is rounded. A partial pool is never published as a whole-position average.</summary>
public sealed record ExactAverageCostPool(ExactCostRatio QuantityKg, decimal AverageCostPerKg)
{
    public ExactCostRatio Value => QuantityKg.Multiply(AverageCostPerKg);

    public ExactAverageCostPool Add(ExactCostRatio quantity, ExactCostRatio value)
    {
        if (quantity.Sign < 0 || value.Sign < 0 || QuantityKg.Sign < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var after = QuantityKg.Add(quantity);
        return after.Sign == 0 ? this : new(after, Value.Add(value).Divide(after).Round(AverageCostPrecision.InternalScale));
    }

    public ExactAverageCostPool Remove(ExactCostRatio quantity, ExactCostRatio? actualValue = null)
    {
        if (quantity.Sign < 0 || quantity.CompareTo(QuantityKg) > 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var after = QuantityKg.Subtract(quantity);
        if (actualValue is null) return new(after, AverageCostPerKg); // An ordinary issue keeps the recorded average.
        var value = Value.Subtract(actualValue);
        if (value.Sign < 0 || after.Sign == 0 && value.Sign != 0)
            throw new InvalidOperationException("The original cost effect does not reconcile with the priced pool.");
        return after.Sign == 0 ? new(after, AverageCostPerKg)
            : new(after, value.Divide(after).Round(AverageCostPrecision.InternalScale));
    }
}
