using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryManufacturingCostExactResultContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Legacy_scalar_only_position_history_keeps_its_exact_value()
    {
        var value = JsonSerializer.Deserialize<InventoryManufacturingCostPositionEffectV1>(
            "{\"locationId\":\"3639da08-536f-442a-878b-082702055764\",\"quantityKg\":2,\"tlValue\":66.67,\"includedInMovingAverage\":true}", Json)!;
        Assert.Equal("2", InventoryExactCostRatioV1.Resolve(value.QuantityKg, value.QuantityKgRatio).Numerator);
        Assert.Equal("6667", InventoryExactCostRatioV1.Resolve(value.TlValue, value.TlValueRatio).Numerator);
    }

    [Fact]
    public void Non_terminating_quantity_round_trips_as_ratio_without_a_scalar_approximation()
    {
        var exact = ExactCostRatio.Of(2m).Divide(3m);
        var value = new InventoryManufacturingCostPositionEffectV1(Guid.NewGuid(),
            InventoryExactCostRatioV1.ScalarOf(exact), 66.67m, true, InventoryExactCostRatioV1.From(exact));
        var restored = JsonSerializer.Deserialize<InventoryManufacturingCostPositionEffectV1>(JsonSerializer.Serialize(value, Json), Json)!;
        Assert.Null(restored.QuantityKg);
        Assert.Equal(0, exact.CompareTo(InventoryExactCostRatioV1.Resolve(restored.QuantityKg, restored.QuantityKgRatio)));
        Assert.Equal(66.67m, restored.TlValue);
    }

    [Fact]
    public void Approximate_or_noncanonical_scalar_ratio_pairs_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryExactCostRatioV1.Resolve(0.6667m, new("2", "3")));
        Assert.Throws<InvalidOperationException>(() => InventoryExactCostRatioV1.Resolve(null, new("4", "6")));
        Assert.Throws<InvalidOperationException>(() => InventoryExactCostRatioV1.Resolve(null, null));
        Assert.Equal(0, InventoryExactCostRatioV1.Resolve(0m, new("0", "1")).Sign);
    }
}
