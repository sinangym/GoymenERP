using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): the kg price calculator against the independent golden ADET_KG_FORMUL_GOLDEN_V1 §1 (row id in each
/// case): amount ÷ kg, exact, rounded once to 0,01 half away from zero; the document figure is Σ amount ÷ Σ kg, never an average.
/// </summary>
public sealed class SalesKilogramPriceGoldenTests
{
    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("K01", "25000.00", "1234.500", "20.25")]
    [InlineData("K02-teorik", "10000.00", "493.800", "20.25")]
    [InlineData("K02-gercek", "10000.00", "500.160", "19.99")]
    [InlineData("K03-fatura2", "15000.00", "748.900", "20.03")]
    [InlineData("K03-siparis", "25000.00", "1249.060", "20.02")] // (19,99 + 20,03) / 2 = 20,01 OLMAZ
    [InlineData("K04", "25000.00", "1209.810", "20.66")]
    [InlineData("K05", "1234.50", "100.000", "12.35")] // tam yarım: 12,34 OLMAZ
    [InlineData("K09", "1234.57", "100.005", "12.35")] // 2 ondalık kg (100,01) paydaya girmez
    [InlineData("K10-teorik", "9000.00", "500.000", "18.00")]
    [InlineData("K10-gercek", "9000.00", "489.750", "18.38")]
    [InlineData("K11", "25000.00", "1234.500", "20.25")] // KDV dahil 30 000,00 → 24,30 OLMAZ
    [InlineData("K12", "22500.00", "1234.500", "18.23")]
    [InlineData("K16", "1000.00", "123.450", "8.10")] // USD/kg, kur kullanılmaz
    [InlineData("K19-teorik", "400.00", "49.380", "8.10")]
    [InlineData("K19-gercek", "400.00", "50.016", "8.00")]
    public void SAT_D436_golden_line_kg_prices(string row, string amount, string kilograms, string expected)
    {
        var price = SalesKilogramPriceCalculator.Line(D(amount), D(kilograms), SalesKilogramPriceState.TheoreticalKgUnavailable);

        Assert.True(price.State == SalesKilogramPriceState.Shown, row);
        Assert.Equal(D(expected), price.Value);
    }

    [Fact]
    public void SAT_D436_golden_K06_and_K08_document_prices_are_sum_over_sum_never_an_average()
    {
        // K06: S1 25 000,00 / 1 234,500 and S2 4 000,00 / 216,050 → 19,99 (satır ortalaması 19,38 OLMAZ).
        Assert.Equal(19.99m, SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(4000m, 216.05m)]).Value);
        Assert.Equal(18.51m, SalesKilogramPriceCalculator.Line(4000m, 216.05m, SalesKilogramPriceState.TheoreticalKgUnavailable).Value);
        // K08: an adet line plus a kg line (theoretical kg = its quantity) → 20,47.
        Assert.Equal(20.47m, SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(10500m, 500m)]).Value);
    }

    [Fact]
    public void SAT_D436_golden_K13_to_K15_and_K17_fail_closed()
    {
        Assert.Equal(SalesKilogramPriceState.TheoreticalKgUnavailable,
            SalesKilogramPriceCalculator.Line(25000m, null, SalesKilogramPriceState.TheoreticalKgUnavailable).State); // K13
        Assert.Equal(SalesKilogramPriceState.WeighedKgUnavailable,
            SalesKilogramPriceCalculator.Line(10000m, null, SalesKilogramPriceState.WeighedKgUnavailable).State); // K14
        Assert.Equal(SalesKilogramPriceState.ZeroKg,
            SalesKilogramPriceCalculator.Line(10000m, 0m, SalesKilogramPriceState.WeighedKgUnavailable).State); // K15
        var k17 = SalesKilogramPriceCalculator.Document([new(25000m, 1234.5m), new(500m, null)]);
        Assert.Equal((SalesKilogramPriceState.DocumentNotAllKilogramLines, (decimal?)null), (k17.State, k17.Value)); // K17 / K18 (N2)
        Assert.Equal(SalesKilogramPriceState.AmountUnavailable,
            SalesKilogramPriceCalculator.Line(null, 10m, SalesKilogramPriceState.TheoreticalKgUnavailable).State);
    }
}
