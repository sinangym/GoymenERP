using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D247 / SAT-D259 / SAT-D261 / SAT-D265: golden examples of the one margin calculator — KDV hariç net sale − purchase cost,
/// percent from the UNROUNDED margin, display to 0,01 TL and 0,01 half away from zero, and every fail-closed state. SENTETIK.
/// </summary>
public sealed class SalesLineMarginCalculatorTests
{
    private static readonly Guid Tl = Guid.Parse("7a7a7a7a-7a7a-4a7a-8a7a-7a7a7a7a7a7a");
    private static readonly Guid OtherCurrency = Guid.Parse("7b7b7b7b-7b7b-4b7b-8b7b-7b7b7b7b7b7b");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Piece = Guid.Parse("67676767-6767-4767-8767-676767676767");

    private static SalesLineMargin Margin(string net, string unitCost, string quantity) => SalesLineMarginCalculator.Calculate(
        new SalesLineMarginInput(Money.Of(decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture), Tl, 2), Kg,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Tl,
            SalesLineMarginCost.Of(decimal.Parse(unitCost, System.Globalization.CultureInfo.InvariantCulture), Tl, Kg)));

    [Theory]
    // net, unit cost (4 decimals at most), quantity, amount (0,01 TL), percent (0,01)
    [InlineData("1000.00", "7.5", "100", "250", "25")]
    [InlineData("1500.00", "12.3456", "2.5", "1469.14", "97.94")]
    [InlineData("100.00", "120", "1", "-20", "-20")]
    [InlineData("100.00", "1", "100", "0", "0")]
    [InlineData("10.00", "10.005", "1", "-0.01", "-0.05")]
    [InlineData("1000.00", "1000.05", "1", "-0.05", "-0.01")]
    public void The_margin_is_the_net_sale_minus_the_purchase_cost_shown_to_the_kurus_and_the_percent_to_two_decimals_half_away_from_zero(
        string net, string unitCost, string quantity, string amount, string percent)
    {
        var margin = Margin(net, unitCost, quantity);

        Assert.Equal(SalesLineMarginState.Computed, margin.State);
        Assert.Null(margin.UnavailableReason);
        Assert.Null(margin.PercentUnavailableReason);
        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), margin.Amount!.Value.Amount);
        Assert.Equal(Tl, margin.Amount.Value.CurrencyId);
        Assert.Equal(SalesLineMarginCalculator.AmountScale, margin.Amount.Value.Scale);
        Assert.Equal(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture), margin.Percent);
    }

    [Fact]
    public void The_percent_comes_from_the_unrounded_margin_amount_never_from_the_rounded_kurus()
    {
        // 10.00 − 3.3335 = 6.6665 → amount 6.67; percent 6.6665 / 10 = 66.665 % → 66.67 (from the rounded 6.67 it would be 66.70).
        var margin = Margin("10.00", "3.3335", "1");

        Assert.Equal(6.67m, margin.Amount!.Value.Amount);
        Assert.Equal(66.67m, margin.Percent);
        Assert.NotEqual(66.70m, margin.Percent);
    }

    [Fact]
    public void A_zero_net_sale_has_a_margin_amount_but_its_percent_is_unavailable_never_a_division_by_zero()
    {
        var margin = Margin("0", "5", "2");

        Assert.Equal(SalesLineMarginState.Computed, margin.State);
        Assert.Equal(-10m, margin.Amount!.Value.Amount);
        Assert.Null(margin.Percent);
        Assert.Equal(SalesLineMarginUnavailableReason.NetSaleNotPositive, margin.PercentUnavailableReason);
    }

    [Theory]
    [InlineData(SalesLineCostUnavailability.SourceMissing, SalesLineMarginUnavailableReason.CostSourceMissing)]
    [InlineData(SalesLineCostUnavailability.Unreadable, SalesLineMarginUnavailableReason.CostUnreadable)]
    [InlineData(SalesLineCostUnavailability.NoSource, SalesLineMarginUnavailableReason.CostNoSource)]
    [InlineData(SalesLineCostUnavailability.ProducedItem, SalesLineMarginUnavailableReason.ProducedItem)]
    [InlineData(SalesLineCostUnavailability.MixedSourceItem, SalesLineMarginUnavailableReason.MixedSourceItem)]
    public void Every_cost_state_without_a_value_leaves_the_margin_unavailable_never_zero(
        SalesLineCostUnavailability unavailability, SalesLineMarginUnavailableReason reason)
    {
        var margin = SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl,
            SalesLineMarginCost.Unavailable(unavailability)));

        AssertUnavailable(reason, margin);
    }

    [Fact]
    public void The_cost_before_any_source_exists_is_source_missing()
    {
        Assert.Equal(SalesLineCostUnavailability.SourceMissing, SalesLineMarginCost.SourceMissing.Unavailability);
        Assert.Null(SalesLineMarginCost.SourceMissing.UnitCost);
        AssertUnavailable(SalesLineMarginUnavailableReason.CostSourceMissing, SalesLineMarginCalculator.Calculate(
            new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl, SalesLineMarginCost.SourceMissing)));
    }

    [Fact]
    public void Undecided_conversions_and_invalid_costs_leave_the_margin_unavailable_in_a_fixed_order()
    {
        var cost = SalesLineMarginCost.Of(5m, Tl, Kg);

        AssertUnavailable(SalesLineMarginUnavailableReason.SalesAmountUnavailable,
            SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(null, Kg, 1m, Tl, cost)));
        AssertUnavailable(SalesLineMarginUnavailableReason.CurrencyConversionUndecided,
            SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(100m, OtherCurrency, 2), Kg, 1m, Tl, cost)));
        AssertUnavailable(SalesLineMarginUnavailableReason.CurrencyConversionUndecided, SalesLineMarginCalculator.Calculate(
            new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl, SalesLineMarginCost.Of(5m, OtherCurrency, Kg))));
        AssertUnavailable(SalesLineMarginUnavailableReason.UnitConversionUndecided, SalesLineMarginCalculator.Calculate(
            new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl, SalesLineMarginCost.Of(5m, Tl, Piece))));
        AssertUnavailable(SalesLineMarginUnavailableReason.CostInvalid, SalesLineMarginCalculator.Calculate(
            new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl, SalesLineMarginCost.Of(5.00001m, Tl, Kg))));
        AssertUnavailable(SalesLineMarginUnavailableReason.CostInvalid, SalesLineMarginCalculator.Calculate(
            new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl, SalesLineMarginCost.Of(-0.0001m, Tl, Kg))));
        // The sale is checked before the cost: a missing sale wins over a missing cost.
        AssertUnavailable(SalesLineMarginUnavailableReason.SalesAmountUnavailable,
            SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(null, Kg, 1m, Tl, SalesLineMarginCost.SourceMissing)));
    }

    [Fact]
    public void A_margin_that_System_Decimal_cannot_hold_exactly_is_out_of_range_never_rounded_or_a_crash()
    {
        var margin = SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(1m, Tl, 2), Kg, 2m, Tl,
            SalesLineMarginCost.Of(decimal.MaxValue, Tl, Kg)));

        AssertUnavailable(SalesLineMarginUnavailableReason.AmountOutOfRange, margin);
    }

    [Fact]
    public void The_calculator_refuses_a_non_positive_quantity_and_an_empty_functional_currency()
    {
        var cost = SalesLineMarginCost.Of(5m, Tl, Kg);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(1m, Tl, 2), Kg, 0m, Tl, cost)));
        Assert.Throws<ArgumentException>(() =>
            SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(1m, Tl, 2), Kg, 1m, Guid.Empty, cost)));
        Assert.Throws<ArgumentException>(() => SalesLineMarginCost.Of(1m, Guid.Empty, Kg));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLineMarginCost.Unavailable((SalesLineCostUnavailability)99));
        Assert.Equal((2, 2, 4), (SalesLineMarginCalculator.AmountScale, SalesLineMarginCalculator.PercentScale,
            SalesLineMarginCalculator.CostScale));
    }

    private static void AssertUnavailable(SalesLineMarginUnavailableReason reason, SalesLineMargin margin)
    {
        Assert.Equal(SalesLineMarginState.Unavailable, margin.State);
        Assert.Equal(reason, margin.UnavailableReason);
        Assert.Null(margin.Amount);
        Assert.Null(margin.Percent);
        Assert.Null(margin.PercentUnavailableReason);
    }
}
