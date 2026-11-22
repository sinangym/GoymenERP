using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the margin calculator's trace is the same run as its result. On the golden
/// rows (SAT-D265 G rows, SAT-D426 D rows, SAT-D438 M08) the trace's last values are exactly the shown amount and percent, and
/// the steps recompose: net − cost = exact margin, unit cost × quantity = cost, foreign net × frozen rate = TL net. SENTETIK.
/// </summary>
public sealed class SalesLineMarginExplanationTests
{
    private static readonly Guid Tl = Guid.Parse("7a7a7a7a-7a7a-4a7a-8a7a-7a7a7a7a7a7a");
    private static readonly Guid OtherCurrency = Guid.Parse("7b7b7b7b-7b7b-4b7b-8b7b-7b7b7b7b7b7b");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Piece = Guid.Parse("67676767-6767-4767-8767-676767676767");

    private static decimal D(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal Value(SalesLineMarginExplained explained, string code) =>
        D(Assert.Single(explained.Steps, step => step.Code == code).Value);

    [Theory]
    // SAT-D265 G rows of SalesLineMarginCalculatorTests: net, unit cost, quantity, amount, percent.
    [InlineData("1000.00", "7.5", "100", "250", "25")]
    [InlineData("1500.00", "12.3456", "2.5", "1469.14", "97.94")]
    [InlineData("100.00", "120", "1", "-20", "-20")]
    [InlineData("10.00", "10.005", "1", "-0.01", "-0.05")]
    [InlineData("1000.00", "1000.05", "1", "-0.05", "-0.01")]
    public void WP11_the_TL_margin_trace_ends_exactly_on_the_shown_amount_and_percent_and_its_steps_recompose(
        string net, string unitCost, string quantity, string amount, string percent)
    {
        var input = new SalesLineMarginInput(Money.Of(D(net), Tl, 2), Kg, D(quantity), Tl, SalesLineMarginCost.Of(D(unitCost), Tl, Kg));

        var explained = SalesLineMarginCalculator.Explain(input);
        var shown = SalesLineMarginCalculator.Calculate(input);

        Assert.Equal((shown.State, shown.Amount, shown.Percent), (explained.Margin.State, explained.Margin.Amount, explained.Margin.Percent));
        Assert.Equal((D(amount), D(percent)), (Value(explained, SalesLineMarginTraceCodes.MarginAmount),
            Value(explained, SalesLineMarginTraceCodes.MarginPercent)));
        Assert.Equal(shown.Amount!.Value.Amount, Value(explained, SalesLineMarginTraceCodes.MarginAmount));
        Assert.Equal(Value(explained, SalesLineMarginTraceCodes.UnitCost) * Value(explained, SalesLineMarginTraceCodes.Quantity),
            Value(explained, SalesLineMarginTraceCodes.TotalCost));
        Assert.Equal(Value(explained, SalesLineMarginTraceCodes.NetSale) - Value(explained, SalesLineMarginTraceCodes.TotalCost),
            Value(explained, SalesLineMarginTraceCodes.MarginExact));
        Assert.Equal(
            [SalesLineMarginTraceCodes.NetSale, SalesLineMarginTraceCodes.UnitCost, SalesLineMarginTraceCodes.Quantity,
                SalesLineMarginTraceCodes.TotalCost, SalesLineMarginTraceCodes.MarginExact, SalesLineMarginTraceCodes.MarginAmount,
                SalesLineMarginTraceCodes.MarginPercent],
            explained.Steps.Select(step => step.Code));
        AssertOperandsAreEarlierSteps(explained);
    }

    [Theory]
    // SAT-D426 golden rows (PR1203): row, foreign net, frozen rate, quantity, TL unit cost, amount, percent or null.
    [InlineData("D01", "1000.00", "34.5678", "100", "300.1234", "4555.46", "13.18")]
    [InlineData("D02", "2500.00", "37.12345678", "50", "1500.5", "17783.64", "19.16")]
    [InlineData("D05", "100.00", "34.12345650", "1", "3412.3407", "0.00", "0.00")]
    [InlineData("D08", "500.00", "37.4512", "20", "1000.0001", "-1274.40", "-6.81")]
    [InlineData("D09", "0.00", "34.5678", "2", "5", "-10.00", null)]
    [InlineData("D11", "12345678.91", "34.56789012", "250000", "1234.5678", "118122122.02", "27.68")]
    public void WP11_the_foreign_sale_trace_shows_the_frozen_rate_and_the_exact_TL_net_and_ends_on_the_shown_figures(
        string row, string net, string rate, string quantity, string unitCost, string amount, string? percent)
    {
        var input = new SalesLineMarginInput(Money.Of(D(net), OtherCurrency, 2), Kg, D(quantity), Tl,
            SalesLineMarginCost.Of(D(unitCost), Tl, Kg), ExchangeRate.Of(OtherCurrency, Tl, D(rate)));

        var explained = SalesLineMarginCalculator.Explain(input);

        Assert.True(explained.Margin.State == SalesLineMarginState.Computed, row);
        Assert.Equal(D(rate), Value(explained, SalesLineMarginTraceCodes.SaleRate));
        Assert.Equal(D(net) * D(rate), Value(explained, SalesLineMarginTraceCodes.NetSaleFunctional));
        Assert.Equal(Value(explained, SalesLineMarginTraceCodes.NetSaleFunctional) - Value(explained, SalesLineMarginTraceCodes.TotalCost),
            Value(explained, SalesLineMarginTraceCodes.MarginExact));
        Assert.Equal(D(amount), Value(explained, SalesLineMarginTraceCodes.MarginAmount));
        Assert.Equal(explained.Margin.Amount!.Value.Amount, Value(explained, SalesLineMarginTraceCodes.MarginAmount));
        if (percent is null)
        {
            Assert.DoesNotContain(explained.Steps, step => step.Code == SalesLineMarginTraceCodes.MarginPercent);
            Assert.Equal(SalesLineMarginUnavailableReason.NetSaleNotPositive, explained.Margin.PercentUnavailableReason);
        }
        else Assert.Equal(D(percent), Value(explained, SalesLineMarginTraceCodes.MarginPercent));
        AssertOperandsAreEarlierSteps(explained);
    }

    [Fact]
    public void WP11_golden_M08_the_piece_trace_shows_the_kg_cost_the_pinned_kg_per_unit_and_the_cost_per_piece()
    {
        // M08: 100 adet × 10,00 (foreign) × 34,5678 = 34 567,80 TL; 19,9936 TL/kg × 12,345 kg/adet → 9 885,70 TL, 28,60 %.
        var input = new SalesLineMarginInput(Money.Of(1000m, OtherCurrency, 2), Piece, 100m, Tl, SalesLineMarginCost.Of(19.9936m, Tl, Kg),
            ExchangeRate.Of(OtherCurrency, Tl, 34.5678m), new SalesLinePieceConversion(Kg, 12.345m));

        var explained = SalesLineMarginCalculator.Explain(input);

        Assert.Equal((9885.70m, 28.60m), (Value(explained, SalesLineMarginTraceCodes.MarginAmount),
            Value(explained, SalesLineMarginTraceCodes.MarginPercent)));
        Assert.Equal(19.9936m * 12.345m, Value(explained, SalesLineMarginTraceCodes.CostPerOrderUnit));
        Assert.Equal(Kg, Assert.Single(explained.Steps, step => step.Code == SalesLineMarginTraceCodes.UnitCost).UnitId);
        Assert.Equal(Value(explained, SalesLineMarginTraceCodes.CostPerOrderUnit) * 100m,
            Value(explained, SalesLineMarginTraceCodes.TotalCost));
        AssertOperandsAreEarlierSteps(explained);
    }

    [Fact]
    public void WP11_an_unavailable_margin_trace_stops_at_the_refusing_check_and_shows_no_figure()
    {
        var explained = SalesLineMarginCalculator.Explain(new SalesLineMarginInput(Money.Of(100m, Tl, 2), Kg, 1m, Tl,
            SalesLineMarginCost.Unavailable(SalesLineCostUnavailability.NoSource)));

        Assert.Equal(SalesLineMarginUnavailableReason.CostNoSource, explained.Margin.UnavailableReason);
        Assert.Equal([SalesLineMarginTraceCodes.NetSale], explained.Steps.Select(step => step.Code));
        Assert.Empty(SalesLineMarginCalculator.Explain(new SalesLineMarginInput(null, Kg, 1m, Tl, SalesLineMarginCost.Of(1m, Tl, Kg))).Steps);
    }

    private static void AssertOperandsAreEarlierSteps(SalesLineMarginExplained explained)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in explained.Steps)
        {
            Assert.All(step.Operands, operand => Assert.Contains(operand, seen));
            Assert.True(seen.Add(step.Code), step.Code);
        }
    }
}
