using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>ALIS F017 section3.8: only net newly opened supply revives an order; historical shares never reopen twice.</summary>
public sealed class PurchaseOrderReceiptAdjustmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (PurchaseOrder Order, PurchaseOrderLine Line, Guid AcceptanceId) Accepted()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var acceptance = Guid.NewGuid();
        order.RecordAcceptance(order.Version, acceptance, line.Id, Kg(40), Now);
        return (order, line, acceptance);
    }

    [Fact]
    public void A_definitive_return_keeps_the_received_schedule_portion_locked()
    {
        var (order, line, acceptance) = Accepted();
        var first = new PurchaseOrderDeliverySliceInput(Guid.NewGuid(), Kg(40), new DateOnly(2026, 11, 20));
        var second = new PurchaseOrderDeliverySliceInput(Guid.NewGuid(), Kg(60), new DateOnly(2026, 11, 30));
        order.SetLineDeliverySchedule(order.Version, line.Id, [first, second], "planner", Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), true, false,
            "operator", "No replacement", Now);
        var version = order.Version;

        var failure = Assert.Throws<PurchasingRuleException>(() => order.SetLineDeliverySchedule(order.Version, line.Id,
            [first with { Quantity = Kg(30) }, second with { Quantity = Kg(70) }], "planner", Now));

        Assert.Equal(PurchasingRuleCodes.ScheduleReceivedPortionLocked, failure.Code);
        Assert.Equal(version, order.Version);
        Assert.Equal(Kg(40), line.DeliverySchedule[0].Quantity);
    }

    [Fact]
    public void Partial_undo_retains_original_share_and_opens_only_the_undone_quantity()
    {
        var (order, line, acceptance) = Accepted();
        var original = Assert.Single(order.Acceptances);
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Hatalı kabul", Now.AddMinutes(1));

        Assert.Same(original, Assert.Single(order.Acceptances));
        Assert.Equal(Kg(40), original.Quantity);
        Assert.Equal((Kg(30), Kg(70), Kg(10)), (line.AcceptedQuantity, line.RemainingQuantity, order.AlreadyUndone(acceptance)));
        Assert.Equal((Kg(10), false), (adjustment.NetOpenedQuantity, adjustment.Revived));
        Assert.Same(adjustment, Assert.Single(order.ReceiptAdjustments));
    }

    [Fact]
    public void Cancelled_order_revives_only_the_net_undo_and_retains_cancelled_share_and_first_cancellation()
    {
        var (order, line, acceptance) = Accepted();
        order.Cancel(order.Version, "first-canceller", "İlk iptal", Now);
        var originalCancellation = order.Cancellation;
        var originalVersion = order.Version;
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Kabul geri alındı", Now.AddMinutes(1));

        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal((Kg(30), Kg(60), Kg(10)), (line.AcceptedQuantity, line.CancelledQuantity, line.RemainingQuantity));
        Assert.Equal((true, Kg(10), originalVersion + 1), (adjustment.Revived, adjustment.NetOpenedQuantity, order.Version));
        Assert.Same(originalCancellation, order.Cancellation);
        Assert.Same(originalCancellation, Assert.Single(order.CancellationHistory).Cancellation);

        order.Cancel(order.Version, "second-canceller", "Yeni açılan payın iptali", Now.AddMinutes(2));
        Assert.Equal((Kg(70), Kg(0)), (line.CancelledQuantity, line.RemainingQuantity));
        Assert.Equal(2, order.CancellationHistory.Count);
        Assert.Equal("first-canceller", order.CancellationHistory[0].Cancellation.CancelledBy);
        Assert.Equal("second-canceller", order.Cancellation!.CancelledBy);
        Assert.Equal(order.Version, order.CancellationHistory[1].OrderVersionAfter);
    }

    [Fact]
    public void A_closed_line_revives_without_returning_its_previously_closed_share()
    {
        var (order, line, acceptance) = Accepted();
        order.CloseLine(order.Version, line.Id, "closer", "Eksik teslim", Now);
        var originalClosure = line.Closure;
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Kabul düzeltmesi", Now.AddMinutes(1));

        Assert.False(line.IsClosed);
        Assert.Same(originalClosure, line.Closure);
        Assert.Equal((Kg(60), Kg(10), true), (line.ClosedQuantity, line.RemainingQuantity, adjustment.Revived));
        order.CloseLine(order.Version, line.Id, "closer", "Yeni kalanın kapanışı", Now.AddMinutes(2));
        Assert.True(line.IsClosed);
        Assert.Equal((Kg(70), Kg(0)), (line.ClosedQuantity, line.RemainingQuantity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_zero_net_correction_preserves_cancelled_or_closed_state(bool closed)
    {
        var (order, line, acceptance) = Accepted();
        if (closed) order.CloseLine(order.Version, line.Id, "closer", "Kapanış", Now);
        else order.Cancel(order.Version, "canceller", "İptal", Now);
        var status = order.Status;
        var closure = line.Closure;
        var cancellation = order.Cancellation;
        var replacement = Guid.NewGuid();
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Yalnız etiket düzeltmesi", Now.AddMinutes(1), replacement, Kg(10));

        Assert.Equal(status, order.Status);
        Assert.Equal(closed, line.IsClosed);
        Assert.Same(closure, line.Closure);
        Assert.Same(cancellation, order.Cancellation);
        Assert.Equal((Kg(40), Kg(0), false, Kg(0)),
            (line.AcceptedQuantity, line.RemainingQuantity, adjustment.Revived, adjustment.NetOpenedQuantity));
        Assert.Equal(2, order.Acceptances.Count);
        Assert.Equal(Kg(10), order.Acceptances.Single(item => item.AcceptanceId == replacement).Quantity);
    }

    [Fact]
    public void Zero_net_correction_does_not_open_the_ordinary_acceptance_gate_of_a_cancelled_order()
    {
        var (order, _, acceptance) = Accepted();
        order.Cancel(order.Version, "canceller", "İptal", Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Etiket düzeltmesi", Now, Guid.NewGuid(), Kg(10));
        Assert.Throws<PurchasingRuleException>(() => order.RecordAcceptance(order.Version, Guid.NewGuid(),
            order.Lines[0].Id, Kg(1), Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_net_increase_in_acceptance_of_a_cancelled_or_closed_line_is_refused_without_mutation(bool closed)
    {
        var (order, line, acceptance) = Accepted();
        if (closed) order.CloseLine(order.Version, line.Id, "closer", "Kapanış", Now);
        else order.Cancel(order.Version, "canceller", "İptal", Now);
        var version = order.Version;
        var exception = Assert.Throws<PurchasingRuleException>(() => order.AdjustReceipt(order.Version,
            Guid.NewGuid(), acceptance, Kg(10), false, false, "operator", "Düzeltme", Now, Guid.NewGuid(), Kg(11)));
        Assert.Equal(PurchasingRuleCodes.AcceptanceExceedsRemaining, exception.Code);
        Assert.Equal(version, order.Version);
        Assert.Equal(Kg(40), line.AcceptedQuantity);
        Assert.Empty(order.ReceiptAdjustments);
        Assert.Single(order.Acceptances);
    }

    [Fact]
    public void A_net_increase_inside_an_approved_remainder_consumes_only_the_increase()
    {
        var (order, line, acceptance) = Accepted();
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), false, false,
            "operator", "Kabul miktarı düzeltmesi", Now, Guid.NewGuid(), Kg(20));
        Assert.Equal((Kg(50), Kg(50), Kg(0)), (line.AcceptedQuantity, line.RemainingQuantity, adjustment.NetOpenedQuantity));
        Assert.False(adjustment.Revived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unmarked_return_preserves_closed_or_cancelled_state_and_never_reopens_supply(bool closed)
    {
        var (order, line, acceptance) = Accepted();
        if (closed) order.CloseLine(order.Version, line.Id, "closer", "Kapanış", Now);
        else order.Cancel(order.Version, "canceller", "İptal", Now);
        var status = order.Status;
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), true, false,
            "operator", "Yenisi beklenmeyen iade", Now);
        Assert.Equal(status, order.Status);
        Assert.Equal(closed, line.IsClosed);
        Assert.Equal((Kg(30), Kg(10), Kg(0), false),
            (line.AcceptedQuantity, line.DefinitiveReturnQuantity, line.RemainingQuantity, adjustment.Revived));
        Assert.True(adjustment.NetOpenedQuantity.IsZero);
    }

    [Fact]
    public void Replacement_expected_return_revives_only_the_returned_share()
    {
        var (order, line, acceptance) = Accepted();
        order.Cancel(order.Version, "canceller", "İptal", Now);
        var adjustment = order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(10), true, true,
            "operator", "Yenisi beklenen iade", Now);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal((Kg(30), Kg(0), Kg(60), Kg(10)),
            (line.AcceptedQuantity, line.DefinitiveReturnQuantity, line.CancelledQuantity, line.RemainingQuantity));
        Assert.True(adjustment.Revived);
    }

    [Fact]
    public void Original_share_can_only_be_undone_once_across_all_adjustment_kinds()
    {
        var (order, line, acceptance) = Accepted();
        order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptance, Kg(30), true, false, "operator", "İade", Now);
        var version = order.Version;
        var exception = Assert.Throws<PurchasingRuleException>(() => order.AdjustReceipt(order.Version,
            Guid.NewGuid(), acceptance, Kg(11), false, false, "operator", "Geri alma", Now));
        Assert.Equal(PurchasingRuleCodes.ReceiptAdjustmentExceedsAcceptance, exception.Code);
        Assert.Equal((version, Kg(10), Kg(30)), (order.Version, line.AcceptedQuantity, line.DefinitiveReturnQuantity));
    }

    [Fact]
    public void Duplicate_adjustment_identity_never_changes_another_original_share()
    {
        var (order, line, acceptance) = Accepted();
        var adjustmentId = Guid.NewGuid();
        order.AdjustReceipt(order.Version, adjustmentId, acceptance, Kg(10), false, false, "operator", "Geri alma", Now);
        var version = order.Version;
        var exception = Assert.Throws<PurchasingRuleException>(() => order.AdjustReceipt(order.Version,
            adjustmentId, acceptance, Kg(10), false, false, "operator", "Geri alma", Now));
        Assert.Equal(PurchasingRuleCodes.ReceiptAdjustmentDuplicate, exception.Code);
        Assert.Equal((version, Kg(30)), (order.Version, line.AcceptedQuantity));
        Assert.Single(order.ReceiptAdjustments);
    }
}
