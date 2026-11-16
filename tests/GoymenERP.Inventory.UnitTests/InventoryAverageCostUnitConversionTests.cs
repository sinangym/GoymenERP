using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D85 / KLN-D88 (STK-SC33-a): the per-kilogram moving-average cost read in another unit. The kilograms per unit is
/// rounded once with the policy's coefficient decimals and rounding (the policy's values, never a default), the cost per
/// unit is the cost per kilogram times that coefficient rounded once at the 4-decimal internal scale, AwayFromZero
/// (SAT-D265 (2)), and a coefficient that rounds to zero gives no figure. SENTETIK data only.
/// </summary>
public sealed class InventoryAverageCostUnitConversionTests
{
    [Fact]
    public void The_cost_per_unit_is_the_cost_per_kilogram_times_the_policy_rounded_coefficient()
    {
        // 7.85 kg per piece at 3 decimals stays 7.850; 12.5 · 7.85 = 98.125.
        var result = AverageCostUnitConversion.Convert(12.5m, 7.85m, 3, MidpointRounding.AwayFromZero);

        Assert.Equal((AverageCostUnitConversionStatus.Converted, 7.85m, (decimal?)98.125m),
            (result.Status, result.Coefficient, result.CostPerUnit));
    }

    [Fact]
    public void The_coefficient_is_rounded_once_with_the_policys_decimals_and_rounding_before_it_multiplies()
    {
        // KLN-D88 example: 1.2345 at 3 decimals AwayFromZero is 1.235; 10 · 1.235 = 12.35 (not 10 · 1.2345 = 12.345).
        var awayFromZero = AverageCostUnitConversion.Convert(10m, 1.2345m, 3, MidpointRounding.AwayFromZero);
        Assert.Equal((1.235m, (decimal?)12.35m), (awayFromZero.Coefficient, awayFromZero.CostPerUnit));

        // The core takes the rounding the policy carries; it decides none itself.
        var toEven = AverageCostUnitConversion.Convert(10m, 1.2345m, 3, MidpointRounding.ToEven);
        Assert.Equal((1.234m, (decimal?)12.34m), (toEven.Coefficient, toEven.CostPerUnit));

        var twoDecimals = AverageCostUnitConversion.Convert(10m, 1.2345m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal((1.23m, (decimal?)12.3m), (twoDecimals.Coefficient, twoDecimals.CostPerUnit));
    }

    [Fact]
    public void The_cost_per_unit_is_rounded_once_to_four_decimals_away_from_zero()
    {
        // 10.0001 · 0.505 = 5.0500505 → 5.0501.
        Assert.Equal(5.0501m, AverageCostUnitConversion.Convert(10.0001m, 0.505m, 3, MidpointRounding.AwayFromZero).CostPerUnit);

        // 0.0001 · 0.5 = 0.00005: AwayFromZero gives 0.0001 (banker's rounding would give 0.0000).
        Assert.Equal(0.0001m, AverageCostUnitConversion.Convert(0.0001m, 0.5m, 3, MidpointRounding.AwayFromZero).CostPerUnit);
    }

    [Fact]
    public void A_zero_cost_converts_to_a_zero_cost_per_unit()
    {
        var result = AverageCostUnitConversion.Convert(0m, 2.5m, 3, MidpointRounding.AwayFromZero);

        Assert.Equal((AverageCostUnitConversionStatus.Converted, 2.5m, (decimal?)0m),
            (result.Status, result.Coefficient, result.CostPerUnit));
    }

    [Fact]
    public void A_coefficient_that_rounds_to_zero_gives_no_figure()
    {
        var result = AverageCostUnitConversion.Convert(12m, 0.0004m, 3, MidpointRounding.AwayFromZero);

        Assert.Equal((AverageCostUnitConversionStatus.CoefficientNotPositive, 0m, (decimal?)null),
            (result.Status, result.Coefficient, result.CostPerUnit));
    }

    [Fact]
    public void Malformed_input_is_refused_as_a_caller_fault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(-1m, 1m, 3, MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1.00001m, 1m, 3, MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1m, 0m, 3, MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1m, -1m, 3, MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1m, 1m, -1, MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1m, 1m, AverageCostUnitConversion.MaximumCoefficientDecimals + 1,
                MidpointRounding.AwayFromZero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AverageCostUnitConversion.Convert(1m, 1m, 3, (MidpointRounding)99));
    }
}
