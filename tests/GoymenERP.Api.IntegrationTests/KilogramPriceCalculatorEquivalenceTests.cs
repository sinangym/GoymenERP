using System.Globalization;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// K3-B P3-12 (SAT-D436 / SAT-D439, golden ADET_KG_FORMUL_GOLDEN_V1 §1): Sales and Purchasing show the same kg price figure. Both
/// module calculators delegate the division to the one shared <see cref="KilogramPriceArithmetic"/>; each keeps only its own
/// wire states. The same golden rows pin the shared arithmetic, both line calculators, Purchasing's exact-rational path and both
/// document (Σ amount ÷ Σ kg) figures, so a drift on either side fails here.
/// </summary>
public sealed class KilogramPriceCalculatorEquivalenceTests
{
    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("K01", "25000.00", "1234.500", "20.25")]
    [InlineData("K02-gercek", "10000.00", "500.160", "19.99")]
    [InlineData("K03-fatura2", "15000.00", "748.900", "20.03")]
    [InlineData("K03-siparis", "25000.00", "1249.060", "20.02")]
    [InlineData("K04", "25000.00", "1209.810", "20.66")]
    [InlineData("K05", "1234.50", "100.000", "12.35")] // tam yarım: 12,34 OLMAZ
    [InlineData("K09", "1234.57", "100.005", "12.35")]
    [InlineData("K10-gercek", "9000.00", "489.750", "18.38")]
    [InlineData("K12", "22500.00", "1234.500", "18.23")]
    [InlineData("K16", "1000.00", "123.450", "8.10")]
    [InlineData("K19-gercek", "400.00", "50.016", "8.00")]
    public void SAT_D436_golden_line_kg_price_is_the_same_figure_in_sales_and_purchasing(string row, string amount, string kilograms,
        string expected)
    {
        var shared = KilogramPriceArithmetic.RoundedQuotient(D(amount), D(kilograms));
        var sales = SalesKilogramPriceCalculator.Line(D(amount), D(kilograms), SalesKilogramPriceState.WeighedKgUnavailable);
        var purchasing = PurchaseKgPrice.Line(D(amount), D(kilograms), PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable);
        var exact = PurchaseKgPrice.Exact(PurchaseExactRatio.Of(D(amount)), PurchaseExactRatio.Of(D(kilograms)),
            PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable);

        Assert.True(sales.State == SalesKilogramPriceState.Shown, row);
        Assert.Equal((D(expected), D(expected), D(expected), D(expected)), (shared, sales.Value!.Value, purchasing.Value!.Value, exact.Value!.Value));
        Assert.Equal(KilogramPriceArithmetic.PriceScale, SalesKilogramPriceCalculator.PriceScale);
        Assert.Equal(KilogramPriceArithmetic.PriceScale, PurchaseKgPrice.Scale);
    }

    [Fact]
    public void SAT_D436_golden_K06_and_K08_document_kg_price_is_sum_over_sum_on_both_sides()
    {
        // K06: 25 000,00 / 1 234,500 + 4 000,00 / 216,050 → 19,99 (the line average 19,38 never); K08: + 10 500,00 / 500 → 20,47.
        Assert.Equal(19.99m, SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(4000m, 216.05m)]).Value);
        Assert.Equal(19.99m, PurchaseKgPrice.Document([new(25000m, 1234.5m), new(4000m, 216.05m)]).Value);
        Assert.Equal(20.47m, SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(10500m, 500m)]).Value);
        Assert.Equal(20.47m, PurchaseKgPrice.Document([new(25000m, 1234.5m), new(10500m, 500m)]).Value);

        // K15 / K17 (N2): neither side shows a figure for zero kg or for a line without kg.
        Assert.Null(SalesKilogramPriceCalculator.Line(10000m, 0m, SalesKilogramPriceState.WeighedKgUnavailable).Value);
        Assert.Null(PurchaseKgPrice.Line(10000m, 0m, PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable).Value);
        Assert.Null(SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(500m, null)]).Value);
        Assert.Null(PurchaseKgPrice.Document([new(25000m, 1234.5m), new(500m, null)]).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => KilogramPriceArithmetic.RoundedQuotient(1m, 0m));
    }
}
