using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D438 (AK-6): a piece line's margin = NetSale − c × k × quantity, exact; c is the kg cost (≤ 4 decimals, G16), k the kg/unit
/// pinned at approval (SAT-D437); c × k is never rounded (TM1). Rows are the independent golden ADET_KG_FORMUL_GOLDEN_V1 M01–M12
/// (row id in each case), M13 and M14. The switch <see cref="SalesDraftService.PieceMarginGoldenAccepted"/> is open (the golden
/// travels in this train); the closed path is pinned through the named parameter: closed, a piece line keeps
/// <c>UnitConversionUndecided</c> and its cost is read in the order unit as before AK-6.
/// </summary>
public sealed class SalesPieceMarginGoldenTests
{
    private static readonly Guid Tl = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly Guid Kg = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Piece = SettingsUnitIdentitiesV1.Piece;

    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static SalesLineMargin PieceLine(string netSale, string cost, string kilogramsPerUnit, string quantity,
        Guid? saleCurrency = null, string? rate = null) =>
        SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(
            Money.Of(D(netSale), saleCurrency ?? Tl, 2), Piece, D(quantity), Tl, SalesLineMarginCost.Of(D(cost), Tl, Kg),
            rate is null ? null : ExchangeRate.Of(saleCurrency!.Value, Tl, D(rate)),
            new SalesLinePieceConversion(Kg, D(kilogramsPerUnit))));

    [Theory]
    [InlineData("M01", "25000.00", "19.9936", "12.345", "100", "317.90", "1.27")]
    [InlineData("M02", "250000.00", "19.9936", "12.345", "1000", "3179.01", "1.27")] // 3 179,00 OLMAZ (TM1)
    [InlineData("M03", "10.00", "3.3300", "1.500", "1", "5.01", "50.05")]
    [InlineData("M07", "25000.00", "19.9936", "12.345", "100", "317.90", "1.27")] // kantar sapması marjı değiştirmez
    [InlineData("M09", "2500.00", "19.9936", "12.3457", "10", "31.65", "1.27")]
    [InlineData("M10", "25000.00", "19.9936", "12.345", "100", "317.90", "1.27")] // kayıtlı k (kart 12,400'e değişti)
    public void SAT_D438_golden_piece_rows_compute_the_margin_from_the_kg_cost_and_the_pinned_kg_per_unit(string row,
        string netSale, string cost, string kilogramsPerUnit, string quantity, string amount, string percent)
    {
        var margin = PieceLine(netSale, cost, kilogramsPerUnit, quantity);

        Assert.True(margin.State == SalesLineMarginState.Computed, row);
        Assert.Equal((D(amount), D(percent)), (margin.Amount!.Value.Amount, margin.Percent!.Value));
    }

    [Fact]
    public void SAT_D438_golden_M04_a_kg_line_is_not_converted()
    {
        var margin = SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(21500m, Tl, 2), Kg, 1000m, Tl,
            SalesLineMarginCost.Of(20.1234m, Tl, Kg)));

        Assert.Equal((1376.60m, 6.40m), (margin.Amount!.Value.Amount, margin.Percent!.Value));
    }

    [Fact]
    public void SAT_D438_golden_M06_a_non_positive_net_sale_has_an_amount_and_no_percent()
    {
        var margin = PieceLine("0.00", "19.9936", "12.345", "2");

        Assert.Equal(-493.64m, margin.Amount!.Value.Amount);
        Assert.Null(margin.Percent);
        Assert.Equal(SalesLineMarginUnavailableReason.NetSaleNotPositive, margin.PercentUnavailableReason);
    }

    [Fact]
    public void SAT_D438_golden_M08_a_foreign_piece_line_converts_the_sale_at_the_frozen_rate()
    {
        var margin = PieceLine("1000.00", "19.9936", "12.345", "100", Usd, "34.5678");

        Assert.Equal((9885.70m, 28.60m), (margin.Amount!.Value.Amount, margin.Percent!.Value));
    }

    [Fact]
    public void SAT_D438_golden_M11_without_a_pinned_kg_per_unit_the_margin_stays_undecided()
    {
        var margin = SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(25000m, Tl, 2), Piece, 100m, Tl,
            SalesLineMarginCost.Of(19.9936m, Tl, Kg)));

        Assert.Equal(SalesLineMarginUnavailableReason.UnitConversionUndecided, margin.UnavailableReason);
    }

    [Fact]
    public void SAT_D438_golden_M12_a_kg_cost_finer_than_four_decimals_is_invalid()
    {
        Assert.Equal(SalesLineMarginUnavailableReason.CostInvalid, PieceLine("25000.00", "5.00001", "12.345", "100").UnavailableReason);
    }

    [Fact]
    public void SAT_D438_a_conversion_for_another_cost_unit_is_never_applied()
    {
        var margin = SalesLineMarginCalculator.Calculate(new SalesLineMarginInput(Money.Of(25000m, Tl, 2), Piece, 100m, Tl,
            SalesLineMarginCost.Of(19.9936m, Tl, Guid.NewGuid()), null, new SalesLinePieceConversion(Kg, 12.345m)));

        Assert.Equal(SalesLineMarginUnavailableReason.UnitConversionUndecided, margin.UnavailableReason);
    }

    [Fact]
    public void SAT_D438_golden_M13_a_zero_kg_cost_is_unreadable_and_never_a_full_margin()
    {
        var snapshot = new SalesMarginCostSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), Kg, 100m, Tl, 0m, null, "{}",
            new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));

        var margin = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(25000m, Tl, 2), Piece, 100m, snapshot, 12.345m,
            pieceMarginOpen: true);

        Assert.Equal(SalesLineMarginUnavailableReason.CostUnreadable, margin.UnavailableReason);
        Assert.Null(margin.Amount);
        Assert.Null(margin.Percent);
    }

    [Fact]
    public void AK6_closed_the_piece_margin_switch_keeps_the_piece_line_undecided_and_open_it_computes_golden_M01()
    {
        Assert.True(SalesDraftService.PieceMarginGoldenAccepted);
        var snapshot = new SalesMarginCostSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), Kg, 100m, Tl, 19.9936m, null, "{}",
            new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));

        var closed = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(25000m, Tl, 2), Piece, 100m, snapshot, 12.345m,
            pieceMarginOpen: false);
        var open = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(25000m, Tl, 2), Piece, 100m, snapshot, 12.345m,
            pieceMarginOpen: true);

        var live = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(25000m, Tl, 2), Piece, 100m, snapshot, 12.345m);

        Assert.Equal(SalesLineMarginUnavailableReason.UnitConversionUndecided, closed.UnavailableReason);
        Assert.Equal((317.90m, 1.27m), (open.Amount!.Value.Amount, open.Percent!.Value)); // golden M01
        Assert.Equal(open, live);
    }
}
