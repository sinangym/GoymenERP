using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryManufacturingCostSplitTests
{
    [Theory]
    [InlineData("100.01", "3", "1", "33.34", "66.67")]
    [InlineData("1.01", "3", "1", "0.34", "0.67")]
    [InlineData("0.01", "100", "99", "0.01", "0")]
    [InlineData("100.01", "3", "0", "0", "100.01")]
    [InlineData("100.01", "3", "3", "100.01", "0")]
    [InlineData("0.01", "2", "1", "0.01", "0")]
    public void Shipped_share_rounds_once_and_the_authoritative_residual_stays_in_stock(string original, string kg, string shipped,
        string expectedConsumed, string expectedRemaining)
    {
        static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = InventoryManufacturingCostSplitCalculatorV1.Split(Number(original), Number(kg), Number(shipped));
        Assert.Equal((Number(expectedConsumed), Number(expectedRemaining)), (result.ConsumedTlAmount, result.RemainingTlAmount));
        Assert.Equal(Number(original), result.ConsumedTlAmount + result.RemainingTlAmount);
    }

    [Fact]
    public void More_than_the_declared_mass_or_an_unrecorded_extra_money_scale_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryManufacturingCostSplitCalculatorV1.Split(1m, 3m, 4m));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryManufacturingCostSplitCalculatorV1.Split(1.001m, 3m, 1m));
    }
}
