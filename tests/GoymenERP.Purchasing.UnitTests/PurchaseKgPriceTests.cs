using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-5 (SAT-D436, SAT-D439): the kg price indicator against the independent golden rows of
/// <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §1 (row ids in each case) and the purchase order read's view of it. The golden
/// travels with this train, so the read switch is on. The realization rows are in <see cref="PurchaseKgPriceRealizationTests"/>.
/// SENTETIK.
/// </summary>
public sealed class PurchaseKgPriceTests
{
    private const string NoTheoretical = PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable;
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lira = Guid.Parse("66666666-6666-4666-8666-666666666666");

    private static decimal? LinePrice(decimal amount, decimal kg) => PurchaseKgPrice.Line(amount, kg, NoTheoretical).Value;

    [Fact]
    public void The_kg_price_indicator_is_on_with_its_golden()
    {
        Assert.True(PurchaseOrderKgPriceViews.PurchaseKgPriceGoldenAccepted);
    }

    [Theory]
    [InlineData("25000", "1234.5", "20.25")] // K01 theoretical; K11: the KDV-exclusive 25 000,00 (30 000,00 → 24,30 OLMAZ)
    [InlineData("10000", "500.16", "19.99")] // K02 real (partial invoice, weighed)
    [InlineData("15000", "748.9", "20.03")] // K03 second invoice real
    [InlineData("25000", "1209.81", "20.66")] // K04 weighing deviation, amount unchanged
    [InlineData("1234.50", "100", "12.35")] // K05 exact half → away from zero (12,34 OLMAZ)
    [InlineData("4000", "216.05", "18.51")] // K06 second line
    [InlineData("1234.57", "100.005", "12.35")] // K09 the 2-decimal kg display never enters the denominator (12,34 OLMAZ)
    [InlineData("9000", "500", "18.00")] // K10 purchase theoretical
    [InlineData("9000", "489.75", "18.38")] // K10 purchase real (weighed at receipt)
    [InlineData("22500", "1234.5", "18.23")] // K12 after a 10 % line discount
    [InlineData("1000", "123.45", "8.10")] // K16 USD document → USD/kg, no rate (280,01 TL/kg OLMAZ)
    [InlineData("400", "50.016", "8.00")] // K19 USD real
    public void A_line_kg_price_is_the_amount_over_the_kilograms_rounded_once_half_away_from_zero(string amount, string kg, string expected)
    {
        Assert.Equal(D(expected), LinePrice(D(amount), D(kg)));
    }

    [Fact]
    public void A_document_kg_price_is_the_sum_of_amounts_over_the_sum_of_kilograms_never_an_average_of_line_prices()
    {
        // K03: 25 000 / (500,160 + 748,900) = 20,02 (the average 20,01 OLMAZ); K06: 29 000 / 1 450,55 = 19,99 (19,38 OLMAZ);
        // K08: a piece line and a kg line, 35 500 / 1 734,5 = 20,47.
        Assert.Equal(20.02m, PurchaseKgPrice.Document([new(10000m, 500.16m), new(15000m, 748.9m)]).Value);
        Assert.Equal(19.99m, PurchaseKgPrice.Document([new(25000m, 1234.5m), new(4000m, 216.05m)]).Value);
        Assert.Equal(20.47m, PurchaseKgPrice.Document([new(25000m, 1234.5m), new(10500m, 500m)]).Value);
    }

    [Fact]
    public void Missing_or_zero_kilograms_and_a_missing_amount_show_no_kg_price()
    {
        // K13 no recorded k; K14 no weighing; K15 zero kg.
        Assert.Equal(NoTheoretical, PurchaseKgPrice.Line(25000m, null, NoTheoretical).UnavailableCode);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable,
            PurchaseKgPrice.Line(25000m, null, PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable).UnavailableCode);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.ZeroKg, PurchaseKgPrice.Line(25000m, 0m, NoTheoretical).UnavailableCode);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.AmountUnavailable, PurchaseKgPrice.Line(null, 10m, NoTheoretical).UnavailableCode);
    }

    [Fact]
    public void The_document_level_is_hidden_unless_every_line_is_a_goods_line_with_its_kilograms()
    {
        // K17: one line without kg; K18 (N2 open): a service line without kg.
        Assert.Equal(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg,
            PurchaseKgPrice.Document([new(25000m, 1234.5m), new(4000m, null)]).UnavailableCode);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg,
            PurchaseKgPrice.Document([new(25000m, 1234.5m), new(500m, 0m, IsGoodsLine: false)]).UnavailableCode);
        Assert.Equal(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg, PurchaseKgPrice.Document([]).UnavailableCode);
    }

    [Fact]
    public void The_order_view_takes_the_recorded_k_for_a_piece_line_and_the_quantity_for_a_kg_line()
    {
        // K08 on an order: piece line 100 × 250,00 with k 12,345 recorded at approval; kg line 500 × 21,00.
        var money = PurchaseOrderMoney.Of(Lira, "TRY", Guid.Parse("77777777-7777-4777-8777-777777777777"), 1, 2);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), money, T0, "SENTETIK-hazirlayan");
        var pieceUnit = Guid.NewGuid();
        var kgUnit = Guid.NewGuid();
        var piece = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), pieceUnit, Quantity.Of(100m, QuantityUnit.Piece), null,
            new PurchaseOrderLinePrice(250m, pieceUnit), T0);
        order.AddLine(2, Guid.NewGuid(), Guid.NewGuid(), kgUnit, Quantity.Of(500m, QuantityUnit.Kilogram), null,
            new PurchaseOrderLinePrice(21m, kgUnit), T0);

        // K13 / TK7: before approval there is no recorded k, so no theoretical kg price.
        Assert.Equal((null, NoTheoretical), (PurchaseOrderKgPriceViews.Line(order, piece)!.TheoreticalKgPrice,
            PurchaseOrderKgPriceViews.Line(order, piece)!.TheoreticalUnavailable));
        Assert.Equal(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg, PurchaseOrderKgPriceViews.Document(order).TheoreticalUnavailable);

        order.Approve(3, "SENTETIK-onaylayan", T0, Lira,
            new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms> { [piece.Id] = new(12.345m, "product.kg-per-unit.v1/sentetik") });

        Assert.Equal("20.25", PurchaseOrderKgPriceViews.Line(order, piece)!.TheoreticalKgPrice);
        var document = PurchaseOrderKgPriceViews.Document(order);
        Assert.Equal(("20.47", (string?)null, (string?)null, PurchaseKgPriceUnavailableCodes.RealizationNotAvailable),
            (document.TheoreticalKgPrice, document.TheoreticalUnavailable, document.RealKgPrice, document.RealUnavailable));
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
