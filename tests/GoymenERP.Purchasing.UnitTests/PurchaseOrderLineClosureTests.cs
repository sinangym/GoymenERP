using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// API-11 (ALIS_F017 v1 §2/§3.6, ALS-D10/D12/D87): the short-delivery closure of an APPROVED order's general line closes its
/// not-yet-accepted remainder (and its technical slices') as a separate <c>closed</c> axis beside the cancellation, so the line's
/// future supply is zero; a reopening returns it. Every closure and reopening carries a reason (ALS-D12; the in-tolerance
/// suggestion waits on OPEN-004 / API-95). SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderLineClosureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";
    private const string Closer = "SENTETIK-kapatan";
    private const string Reason = "SENTETIK tedarikçi kalanı gönderemiyor";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static (PurchaseOrder Order, PurchaseOrderLine Line, PurchaseOrderLine Other) Approved()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var line = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1_000m), T0);
        var other = order.AddLine(2, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(500m), T0);
        order.Approve(3, Approver, T0);
        return (order, line, other);
    }

    [Fact]
    public void Closing_after_a_partial_acceptance_closes_only_the_remainder_and_leaves_the_other_line_open()
    {
        var (order, line, other) = Approved();
        order.RecordAcceptance(4, Guid.NewGuid(), line.Id, Kg(400m), T0.AddHours(1));

        order.CloseLine(5, line.Id, Closer, Reason, T0.AddHours(2));

        Assert.Equal((PurchaseOrderStatus.Approved, 6L), (order.Status, order.Version));
        Assert.Equal((Kg(400m), Kg(0m), Kg(600m), Kg(0m)),
            (line.AcceptedQuantity, line.CancelledQuantity, line.ClosedQuantity, line.RemainingQuantity));
        Assert.True(line.IsClosed);
        Assert.Equal((Closer, Reason, TimeSpan.Zero),
            (line.Closure!.ClosedBy, line.Closure.Reason, line.Closure.ClosedAtUtc.Offset));
        Assert.Equal(new PurchaseOrderLineClosureChange(true, Closer, T0.AddHours(2).ToUniversalTime(), Reason), line.LastClosureChange);
        // The ordered quantity, the commitment and the other line stay; the line's future supply leaves the order's remaining commitment.
        Assert.Equal((Kg(1_000m), Kg(1_000m)), (line.Quantity, line.CommittedQuantity));
        Assert.False(other.IsClosed);
        Assert.Null(other.LastClosureChange);
        Assert.Equal(Kg(500m), order.RemainingCommitment(QuantityUnit.Kilogram));
    }

    [Fact]
    public void Closing_a_broken_down_line_closes_every_technical_remainder_and_reopening_returns_them()
    {
        var (order, line, _) = Approved();
        var technical = order.BreakDown(4, line.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(300m), T0);
        var accepted = order.BreakDown(5, line.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(100m), T0);
        order.RecordAcceptance(6, Guid.NewGuid(), technical.Id, Kg(50m), T0);
        order.RecordAcceptance(7, Guid.NewGuid(), accepted.Id, Kg(100m), T0);

        order.CloseLine(8, line.Id, Closer, Reason, T0);

        Assert.Equal((Kg(600m), Kg(0m)), (line.ClosedQuantity, line.RemainingQuantity));
        Assert.Equal((Kg(250m), Kg(0m), true), (technical.ClosedQuantity, technical.RemainingQuantity, technical.IsClosed));
        // A fully accepted slice closes zero and is not reported as closed; the general line carries the record.
        Assert.Equal((Kg(0m), false), (accepted.ClosedQuantity, accepted.IsClosed));
        Assert.Null(technical.Closure);
        Assert.Null(technical.LastClosureChange);

        order.ReopenLine(9, line.Id, Closer, "SENTETIK tedarikçi kalanı gönderecek", T0.AddDays(1));

        Assert.Equal((10L, false, false), (order.Version, line.IsClosed, technical.IsClosed));
        Assert.Equal((Kg(600m), Kg(250m), Kg(0m)), (line.RemainingQuantity, technical.RemainingQuantity, accepted.RemainingQuantity));
        Assert.Equal((Kg(0m), Kg(0m)), (line.ClosedQuantity, technical.ClosedQuantity));
        Assert.Null(line.Closure);
        Assert.Equal(new PurchaseOrderLineClosureChange(false, Closer, T0.AddDays(1).ToUniversalTime(), "SENTETIK tedarikçi kalanı gönderecek"),
            line.LastClosureChange);
    }

    [Fact]
    public void A_closed_line_takes_no_acceptance_or_breakdown_until_it_is_reopened()
    {
        var (order, line, _) = Approved();
        order.CloseLine(4, line.Id, Closer, Reason, T0);

        Assert.Equal(PurchasingRuleCodes.AcceptanceExceedsRemaining,
            CodeOf(() => order.RecordAcceptance(5, Guid.NewGuid(), line.Id, Kg(1m), T0)));
        Assert.Equal(PurchasingRuleCodes.SliceExceedsRemaining,
            CodeOf(() => order.BreakDown(5, line.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), T0)));

        order.ReopenLine(5, line.Id, Closer, Reason, T0);
        order.RecordAcceptance(6, Guid.NewGuid(), line.Id, Kg(1_000m), T0);
        Assert.Equal(Kg(0m), line.RemainingQuantity);
    }

    [Fact]
    public void Cancelling_an_order_with_a_closed_line_cancels_only_the_open_remainders_and_the_line_stays_closed()
    {
        var (order, line, other) = Approved();
        order.CloseLine(4, line.Id, Closer, Reason, T0);

        order.Cancel(5, Closer, "SENTETIK", T0);

        Assert.Equal((Kg(1_000m), Kg(0m)), (line.ClosedQuantity, line.CancelledQuantity));
        Assert.Equal((Kg(0m), Kg(500m)), (other.ClosedQuantity, other.CancelledQuantity));
        // Fail-closed (§2 Cancelled ∩ closed): a cancelled order's line is neither closed nor reopened here.
        Assert.Equal(PurchasingRuleCodes.OrderCancelled, CodeOf(() => order.ReopenLine(6, line.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.OrderCancelled, CodeOf(() => order.CloseLine(6, other.Id, Closer, Reason, T0)));
    }

    [Fact]
    public void The_refusals_change_nothing()
    {
        var draft = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var draftLine = draft.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(10m), T0);
        Assert.Equal(PurchasingRuleCodes.LineNotClosable, CodeOf(() => draft.CloseLine(2, draftLine.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineNotClosable, CodeOf(() => draft.ReopenLine(2, draftLine.Id, Closer, Reason, T0)));

        var (order, line, other) = Approved();
        var technical = order.BreakDown(4, line.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(10m), T0);
        order.RecordAcceptance(5, Guid.NewGuid(), other.Id, Kg(500m), T0);

        Assert.Throws<PurchasingVersionConflictException>(() => order.CloseLine(5, line.Id, Closer, Reason, T0));
        Assert.Equal(PurchasingRuleCodes.LineNotFound, CodeOf(() => order.CloseLine(6, Guid.NewGuid(), Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineClosureOnTechnicalLine, CodeOf(() => order.CloseLine(6, technical.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineNothingToClose, CodeOf(() => order.CloseLine(6, other.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineNotClosed, CodeOf(() => order.ReopenLine(6, line.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineClosureReasonRequired, CodeOf(() => order.CloseLine(6, line.Id, Closer, null, T0)));
        Assert.Equal(PurchasingRuleCodes.LineClosureReasonRequired, CodeOf(() => order.CloseLine(6, line.Id, Closer, "   ", T0)));
        Assert.Equal(PurchasingRuleCodes.LineClosureReasonTooLong,
            CodeOf(() => order.CloseLine(6, line.Id, Closer, new string('x', PurchaseOrder.MaximumReasonLength + 1), T0)));
        Assert.Throws<ArgumentException>(() => order.CloseLine(6, line.Id, "  ", Reason, T0));
        Assert.Throws<ArgumentException>(() => order.CloseLine(6, line.Id, $" {Closer}", Reason, T0));
        Assert.Equal((6L, false, false), (order.Version, line.IsClosed, technical.IsClosed));
        Assert.Null(line.LastClosureChange);

        order.CloseLine(6, line.Id, Closer, new string('x', PurchaseOrder.MaximumReasonLength), T0);
        Assert.Equal(PurchasingRuleCodes.LineAlreadyClosed, CodeOf(() => order.CloseLine(7, line.Id, Closer, Reason, T0)));
        Assert.Equal(PurchasingRuleCodes.LineReopenReasonRequired, CodeOf(() => order.ReopenLine(7, line.Id, Closer, "", T0)));
        Assert.Equal(PurchasingRuleCodes.LineReopenReasonTooLong,
            CodeOf(() => order.ReopenLine(7, line.Id, Closer, new string('x', PurchaseOrder.MaximumReasonLength + 1), T0)));
        Assert.Equal(PurchasingRuleCodes.LineClosureOnTechnicalLine, CodeOf(() => order.ReopenLine(7, technical.Id, Closer, Reason, T0)));
        Assert.Equal((7L, true), (order.Version, line.IsClosed));
    }

    [Fact]
    public void The_store_replays_closures_and_reopenings_between_the_acceptances_and_refuses_a_history_that_disagrees()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero);
        var row = new PurchaseOrderRow
        {
            Id = orderId, CompanyId = Company, SupplierPartyId = Supplier, CompanyAccountId = Guid.NewGuid(), Status = (int)PurchaseOrderStatus.Approved,
            Version = 5, EffectiveOn = new DateOnly(2026, 11, 14), CreatedAtUtc = at, UpdatedAtUtc = at, CreatedBy = Preparer, CorrelationId = "SENTETIK",
            ApprovedBy = Approver, ApprovedAtUtc = at,
        };
        var line = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 1, LineId = lineId, Kind = (int)PurchaseOrderLineKind.General, ProductId = Guid.NewGuid(),
            ProductVersion = "7", UnitId = KgUnit, UnitIsKilogram = true, Ordered = 10m, Technical = 0m, Accepted = 4m, Cancelled = 0m, Closed = 6m,
        };
        var acceptance = new PurchaseAcceptanceRow
        {
            AcceptanceId = Guid.NewGuid(), CompanyId = Company, OrderId = orderId, LineId = lineId, Quantity = 4m, OrderVersionAfter = 4, RecordedAtUtc = at,
            IdempotencyKeyHash = new string('A', 64), RequestHash = new string('B', 64), ActorId = "SENTETIK", CorrelationId = "SENTETIK", ResultJson = "{}",
        };
        PurchaseOrderLineClosureEventRow Event(long version, bool closed) => new()
        {
            CompanyId = Company, OrderId = orderId, LineId = lineId, OrderVersionAfter = version, Closed = closed, Reason = Reason,
            RecordedBy = Closer, RecordedAtUtc = at.AddHours(version), CorrelationId = "SENTETIK",
        };
        // v2: line; v3: approval; v4: acceptance; v5: closure.
        var closures = new List<PurchaseOrderLineClosureEventRow> { Event(5, true) };

        var rebuilt = PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures);

        Assert.Equal((PurchaseOrderStatus.Approved, 5L), (rebuilt.Status, rebuilt.Version));
        Assert.Equal((Kg(4m), Kg(6m), Kg(0m), true),
            (rebuilt.Lines[0].AcceptedQuantity, rebuilt.Lines[0].ClosedQuantity, rebuilt.Lines[0].RemainingQuantity, rebuilt.Lines[0].IsClosed));
        Assert.Equal(new PurchaseOrderLineClosure(Closer, at.AddHours(5), Reason), rebuilt.Lines[0].Closure);

        // v6: the reopening; the stored line is open again and its Closed total is zero.
        closures.Insert(0, Event(6, false));
        row.Version = 6;
        line.Closed = 0m;
        var reopened = PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures);
        Assert.Equal((6L, false, Kg(6m)), (reopened.Version, reopened.Lines[0].IsClosed, reopened.Lines[0].RemainingQuantity));

        // A stored Closed total the replay does not reach is refused.
        line.Closed = 6m;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures));
        line.Closed = 0m;

        // A closure off the version chain, of another company or of an unknown line is refused.
        closures[0].OrderVersionAfter = 7;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures));
        closures[0].OrderVersionAfter = 6;
        closures[1].CompanyId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures));
        closures[1].CompanyId = Company;
        closures[1].LineId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures));
        closures[1].LineId = lineId;

        // The domain's rules apply again: two closures in a row, or a closure without a reason, are refused on rebuild.
        closures[0].Closed = true;
        Assert.Equal(PurchasingRuleCodes.LineAlreadyClosed,
            Assert.Throws<PurchasingRuleException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures)).Code);
        closures[0].Closed = false;
        closures[1].Reason = " ";
        Assert.Equal(PurchasingRuleCodes.LineClosureReasonRequired,
            Assert.Throws<PurchasingRuleException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], null, closures)).Code);
    }
}
