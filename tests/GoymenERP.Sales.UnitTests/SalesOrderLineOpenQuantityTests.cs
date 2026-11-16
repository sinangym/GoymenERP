using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// KLN-D100 (SINAN-CEVAP 34.23): the open quantity is the line quantity minus the delivered (shipped) quantity corrected by the
/// approved customer returns; the invoiced quantity never sets it and only feeds the separate "delivered not invoiced" figure.
/// Every value is SENTETIK.
/// </summary>
public sealed class SalesOrderLineOpenQuantityTests
{
    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static Quantity Pieces(decimal value) => Quantity.Of(value, QuantityUnit.Piece);

    [Fact]
    public void The_open_quantity_is_ordered_minus_delivered_and_the_invoiced_quantity_does_not_change_it()
    {
        var notInvoiced = SalesOrderLineOpenQuantity.Calculate(Kg(1000m), Kg(400.25m), Kg(0m), Kg(0m));
        var partlyInvoiced = SalesOrderLineOpenQuantity.Calculate(Kg(1000m), Kg(400.25m), Kg(0m), Kg(150m));
        var fullyInvoiced = SalesOrderLineOpenQuantity.Calculate(Kg(1000m), Kg(400.25m), Kg(0m), Kg(400.25m));

        Assert.All(new[] { notInvoiced, partlyInvoiced, fullyInvoiced }, result => Assert.Equal(599.75m, result.Open.Value));
        Assert.Equal(400.25m, notInvoiced.DeliveredNotInvoiced!.Value.Value);
        Assert.Equal(250.25m, partlyInvoiced.DeliveredNotInvoiced!.Value.Value);
        Assert.Equal(0m, fullyInvoiced.DeliveredNotInvoiced!.Value.Value);
        Assert.Equal(0m, fullyInvoiced.Excess.Value);
    }

    [Fact]
    public void An_approved_customer_return_corrects_the_delivered_quantity_and_reopens_what_it_takes_back()
    {
        var result = SalesOrderLineOpenQuantity.Calculate(Kg(1000m), Kg(1000m), Kg(125.5m), Kg(1000m));

        Assert.Equal(874.5m, result.NetDelivered.Value);
        Assert.Equal(125.5m, result.Open.Value);
        // Invoiced above the net delivered (a return after invoicing) leaves nothing "delivered not invoiced"; the credit is the
        // return's own document, not a negative figure here.
        Assert.Equal(0m, result.DeliveredNotInvoiced!.Value.Value);
        Assert.Equal("874.5", result.NetDelivered.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Net_delivered_above_the_order_is_excess_with_nothing_open()
    {
        var result = SalesOrderLineOpenQuantity.Calculate(Pieces(10m), Pieces(13m), Pieces(1m), null);

        Assert.Equal(0m, result.Open.Value);
        Assert.Equal(2m, result.Excess.Value);
        Assert.Equal(QuantityUnit.Piece, result.Open.Unit);
    }

    [Fact]
    public void An_unknown_invoiced_quantity_is_no_figure_never_zero()
    {
        var result = SalesOrderLineOpenQuantity.Calculate(Kg(10m), Kg(4m), Kg(0m), null);

        Assert.Null(result.Invoiced);
        Assert.Null(result.DeliveredNotInvoiced);
        Assert.Equal(6m, result.Open.Value);
    }

    [Fact]
    public void A_return_above_the_delivered_total_or_another_unit_is_refused_instead_of_shown()
    {
        var aboveDelivered = Assert.Throws<SalesRuleException>(() =>
            SalesOrderLineOpenQuantity.Calculate(Kg(10m), Kg(4m), Kg(4.001m), null));
        Assert.Equal(SalesRuleCodes.OpenQuantityReturnAboveDelivered, aboveDelivered.Code);

        Assert.Equal(SalesRuleCodes.UnitMismatch, Assert.Throws<SalesRuleException>(() =>
            SalesOrderLineOpenQuantity.Calculate(Kg(10m), Pieces(4m), Kg(0m), null)).Code);
        Assert.Equal(SalesRuleCodes.UnitMismatch, Assert.Throws<SalesRuleException>(() =>
            SalesOrderLineOpenQuantity.Calculate(Kg(10m), Kg(4m), Kg(0m), Pieces(1m))).Code);
    }
}
