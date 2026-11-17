using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseReceiptAdjustmentBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (PurchaseOrder Order, PurchaseOrderLine Line, Guid First, Guid Second) Received()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        order.RecordAcceptance(order.Version, first, line.Id, Kg(20), Now);
        order.RecordAcceptance(order.Version, second, line.Id, Kg(20), Now);
        return (order, line, first, second);
    }

    [Fact]
    public void Net_zero_batch_on_a_cancelled_order_never_exposes_an_individual_over_or_under_acceptance()
    {
        var (order, line, first, second) = Received();
        order.Cancel(order.Version, "canceller", "Cancelled remainder", Now);
        var version = order.Version;
        var original = order.Acceptances.ToArray();
        var result = order.AdjustReceipts(version,
            [new(Guid.NewGuid(), first, Kg(10), false, false, Guid.NewGuid(), Kg(20)),
             new(Guid.NewGuid(), second, Kg(20), false, false, Guid.NewGuid(), Kg(10))],
            "operator", "One net correction", Now);

        Assert.Equal((version + 1, PurchaseOrderStatus.Cancelled, Kg(40), Kg(60), Kg(0)),
            (order.Version, order.Status, line.AcceptedQuantity, line.CancelledQuantity, line.RemainingQuantity));
        Assert.All(result.Adjustments, item =>
        {
            Assert.Equal(version + 1, item.OrderVersionAfter);
            Assert.False(item.Revived);
            Assert.Equal(Kg(0), item.NetOpenedQuantity);
        });
        Assert.Equal((Kg(0), Kg(0), false),
            (result.Supplies[0].NetOpenedQuantity, result.Supplies[0].AllocatableQuantity, result.Supplies[0].Revived));
        Assert.Same(original[0], order.Acceptances[0]); Assert.Same(original[1], order.Acceptances[1]);
    }

    [Fact]
    public void Positive_group_net_opens_once_and_line_contributions_do_not_duplicate_the_group_budget()
    {
        var (order, line, first, second) = Received();
        order.Cancel(order.Version, "canceller", "Cancelled remainder", Now);
        var result = order.AdjustReceipts(order.Version,
            [new(Guid.NewGuid(), first, Kg(10), false, false, Guid.NewGuid(), Kg(15)),
             new(Guid.NewGuid(), second, Kg(10), false, false)], "operator", "Net five", Now);

        Assert.Equal((PurchaseOrderStatus.Approved, Kg(35), Kg(60), Kg(5)),
            (order.Status, line.AcceptedQuantity, line.CancelledQuantity, line.RemainingQuantity));
        Assert.Equal((Kg(5), Kg(5), true),
            (result.Supplies[0].NetOpenedQuantity, result.Supplies[0].AllocatableQuantity, result.Supplies[0].Revived));
        Assert.Equal(5m, result.Adjustments.Sum(item => item.NetOpenedQuantity.Value));
        Assert.Equal(Kg(0), result.Adjustments[0].NetOpenedQuantity);
        Assert.Equal(Kg(5), result.Adjustments[1].NetOpenedQuantity);
    }

    [Fact]
    public void A_late_invalid_original_share_leaves_every_earlier_line_and_the_version_untouched()
    {
        var (order, line, first, second) = Received();
        var version = order.Version;
        var failure = Assert.Throws<PurchasingRuleException>(() => order.AdjustReceipts(version,
            [new(Guid.NewGuid(), first, Kg(10), false, false), new(Guid.NewGuid(), second, Kg(21), false, false)],
            "operator", "Invalid last line", Now));

        Assert.Equal(PurchasingRuleCodes.ReceiptAdjustmentExceedsAcceptance, failure.Code);
        Assert.Equal((version, Kg(40), Kg(60)), (order.Version, line.AcceptedQuantity, line.RemainingQuantity));
        Assert.Empty(order.ReceiptAdjustments);
        Assert.Equal(2, order.Acceptances.Count);
    }

    [Fact]
    public void Repeated_original_ids_share_one_remaining_undo_cap_before_any_mutation()
    {
        var (order, line, first, _) = Received();
        var version = order.Version;
        var failure = Assert.Throws<PurchasingRuleException>(() => order.AdjustReceipts(version,
            [new(Guid.NewGuid(), first, Kg(11), false, false), new(Guid.NewGuid(), first, Kg(10), false, false)],
            "operator", "Cumulative cap", Now));

        Assert.Equal(PurchasingRuleCodes.ReceiptAdjustmentExceedsAcceptance, failure.Code);
        Assert.Equal((version, Kg(40)), (order.Version, line.AcceptedQuantity));
        Assert.Empty(order.ReceiptAdjustments);
    }
}
