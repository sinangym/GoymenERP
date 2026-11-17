using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ProductionExactCurrencyProofTests
{
    [Fact]
    public void Original_foreign_unit_ratio_is_lossless_after_a_return_and_never_revalues_the_stamped_tl_issue()
    {
        var issue = Guid.NewGuid(); var currency = Guid.NewGuid(); var ratio = new ProductionExactRatioV1("1", "3");
        var input = new ProductionReceiptCostInput(true,
            [new(issue, new Kilograms(3m), IssueCostReadStatus.Value, 10m, new(IssueCostCurrencyStatus.Converted, currency, null, 40m, ratio))],
            [new(Guid.NewGuid(), issue, new Kilograms(1m))], new Kilograms(2m), Kilograms.Zero, Kilograms.Zero, [], Kilograms.Zero,
            ScrapCostSettingStatus.On, NonKgInputUse.None);
        var result = ProductionReceiptCostCalculatorV1.Calculate(input);
        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status); Assert.Equal(20m, result.NetInputCost);
        var line = Assert.Single(result.CurrencyLines);
        Assert.Null(line.ForeignUnitCostPerKg); Assert.Null(line.ForeignAmount);
        Assert.Equal(ratio, line.ForeignUnitCostRatio); Assert.Equal(new("2", "3"), line.ForeignAmountRatio);
        Assert.Equal(10m, line.TlUnitCostPerKg); Assert.Equal(40m, line.ExchangeRate);
    }

    [Theory]
    [InlineData("1", "3", true)] [InlineData("0", "1", false)] [InlineData("1", "0", false)]
    [InlineData("2", "6", false)] [InlineData("01", "3", false)] [InlineData("-1", "3", false)]
    public void Exact_currency_proof_requires_positive_normalized_canonical_integer_pairs(string numerator, string denominator, bool valid) =>
        Assert.Equal(valid, new ProductionExactRatioV1(numerator, denominator).IsValid());

    [Fact]
    public void Exact_currency_display_amount_multiplies_the_decimal_quantity_as_an_integer_ratio()
    {
        var ratio = new ProductionExactRatioV1("1", "3");
        Assert.Equal(new("1", "6"), ratio.Multiply(0.5m)); Assert.Equal(new("0", "1"), ratio.Multiply(0m));
        Assert.Equal(new("10000000000000000000000000001", "30000000000000000000000000000"), ratio.Multiply(1.0000000000000000000000000001m));
    }
}
