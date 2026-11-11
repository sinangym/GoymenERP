using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-3 (plan API-10; No.161 §2, Sinan 2026-10-27): an APPROVED order's whole not-yet-accepted remainder is cancelled — every line,
/// even after a partial acceptance; no line- or quantity-level partial cancel. The remainder leaves the future supply (master
/// INV-006: Onaylı − İptal − KabulNet), the accepted shares stay, and a cancelled order takes no acceptance or breakdown.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderCancellationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PieceUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Canceller = "SENTETIK-canceller";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static Quantity Pieces(decimal value) => Quantity.Of(value, QuantityUnit.Piece);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static (PurchaseOrder Order, PurchaseOrderLine Kg, PurchaseOrderLine Pieces) Approved()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var kg = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(100_000m), T0);
        var pieces = order.AddLine(2, Guid.NewGuid(), Guid.NewGuid(), PieceUnit, Pieces(40m), T0);
        order.Approve(3, Approver, T0);
        return (order, kg, pieces);
    }

    [Fact]
    public void After_a_partial_acceptance_the_whole_remainder_of_every_line_is_cancelled_and_the_accepted_share_stays()
    {
        var (order, kg, pieces) = Approved();
        order.RecordAcceptance(4, Guid.NewGuid(), kg.Id, Kg(40_000m), T0.AddHours(1));

        order.Cancel(5, Canceller, "SENTETIK tedarikçi kalanı gönderemiyor", T0.AddHours(2));

        Assert.Equal((PurchaseOrderStatus.Cancelled, 6L), (order.Status, order.Version));
        Assert.Equal((Kg(40_000m), Kg(60_000m), Kg(0m)), (kg.AcceptedQuantity, kg.CancelledQuantity, kg.RemainingQuantity));
        Assert.Equal((Pieces(0m), Pieces(40m), Pieces(0m)), (pieces.AcceptedQuantity, pieces.CancelledQuantity, pieces.RemainingQuantity));
        // INV-006: the future supply is gone in every unit; the commitment and the acceptance history are untouched.
        Assert.Equal(Kg(0m), order.RemainingCommitment(QuantityUnit.Kilogram));
        Assert.Equal(Pieces(0m), order.RemainingCommitment(QuantityUnit.Piece));
        Assert.Equal((Kg(100_000m), Kg(100_000m)), (kg.Quantity, kg.CommittedQuantity));
        Assert.Single(order.Acceptances);
        Assert.Equal((Canceller, "SENTETIK tedarikçi kalanı gönderemiyor", TimeSpan.Zero),
            (order.Cancellation!.CancelledBy, order.Cancellation.Reason, order.Cancellation.CancelledAtUtc.Offset));
    }

    [Fact]
    public void A_broken_down_order_cancels_the_general_and_the_technical_remainders_each_in_its_own_line()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var general = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1_000m), T0);
        order.Approve(2, Approver, T0);
        var technical = order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);
        order.RecordAcceptance(4, Guid.NewGuid(), technical.Id, Kg(25m), T0);

        order.Cancel(5, Canceller, "SENTETIK", T0);

        Assert.Equal((Kg(925m), Kg(0m)), (general.CancelledQuantity, general.RemainingQuantity));
        Assert.Equal((Kg(50m), Kg(25m), Kg(0m)), (technical.CancelledQuantity, technical.AcceptedQuantity, technical.RemainingQuantity));
        Assert.Equal(Kg(0m), order.RemainingCommitment(QuantityUnit.Kilogram));
    }

    [Fact]
    public void A_line_already_fully_accepted_cancels_zero_while_another_line_still_has_a_remainder()
    {
        var (order, kg, pieces) = Approved();
        order.RecordAcceptance(4, Guid.NewGuid(), pieces.Id, Pieces(40m), T0);

        order.Cancel(5, Canceller, "SENTETIK", T0);

        Assert.Equal((Pieces(40m), Pieces(0m)), (pieces.AcceptedQuantity, pieces.CancelledQuantity));
        Assert.Equal(Kg(100_000m), kg.CancelledQuantity);
    }

    [Fact]
    public void A_draft_or_an_already_cancelled_order_is_not_cancellable_and_nothing_moves()
    {
        var draft = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        draft.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(5m), T0);
        Assert.Equal(PurchasingRuleCodes.OrderNotCancellable, CodeOf(() => draft.Cancel(2, Canceller, "SENTETIK", T0)));
        Assert.Equal((PurchaseOrderStatus.Draft, 2L, null), (draft.Status, draft.Version, draft.Cancellation));

        var (order, kg, _) = Approved();
        order.Cancel(4, Canceller, "SENTETIK", T0);
        Assert.Equal(PurchasingRuleCodes.OrderNotCancellable, CodeOf(() => order.Cancel(5, Canceller, "SENTETIK again", T0)));
        Assert.Equal((5L, Kg(100_000m), "SENTETIK"), (order.Version, kg.CancelledQuantity, order.Cancellation!.Reason));
    }

    [Fact]
    public void A_fully_accepted_order_has_nothing_to_cancel_and_the_refusal_records_nothing()
    {
        var (order, kg, pieces) = Approved();
        order.RecordAcceptance(4, Guid.NewGuid(), kg.Id, Kg(100_000m), T0);
        order.RecordAcceptance(5, Guid.NewGuid(), pieces.Id, Pieces(40m), T0);

        Assert.Equal(PurchasingRuleCodes.OrderNothingToCancel, CodeOf(() => order.Cancel(6, Canceller, "SENTETIK", T0)));
        Assert.Equal((PurchaseOrderStatus.Approved, 6L, null), (order.Status, order.Version, order.Cancellation));
        Assert.Equal((Kg(0m), Pieces(0m)), (kg.CancelledQuantity, pieces.CancelledQuantity));
    }

    [Theory]
    [InlineData(null, PurchasingRuleCodes.OrderCancellationReasonRequired)]
    [InlineData("", PurchasingRuleCodes.OrderCancellationReasonRequired)]
    [InlineData("   ", PurchasingRuleCodes.OrderCancellationReasonRequired)]
    public void The_reason_is_required(string? reason, string code)
    {
        var (order, kg, _) = Approved();
        Assert.Equal(code, CodeOf(() => order.Cancel(4, Canceller, reason, T0)));
        Assert.Equal((PurchaseOrderStatus.Approved, Kg(0m)), (order.Status, kg.CancelledQuantity));
    }

    [Fact]
    public void A_reason_over_the_bound_is_refused_never_cut_and_one_at_the_bound_is_kept_exactly()
    {
        var (order, _, _) = Approved();
        Assert.Equal(PurchasingRuleCodes.OrderCancellationReasonTooLong,
            CodeOf(() => order.Cancel(4, Canceller, new string('x', PurchaseOrder.MaximumReasonLength + 1), T0)));

        var bound = new string('y', PurchaseOrder.MaximumReasonLength);
        order.Cancel(4, Canceller, bound, T0);
        Assert.Equal(bound, order.Cancellation!.Reason);
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict_and_a_malformed_actor_an_argument_error()
    {
        var (order, _, _) = Approved();
        Assert.Throws<PurchasingVersionConflictException>(() => order.Cancel(3, Canceller, "SENTETIK", T0));
        Assert.Throws<ArgumentException>(() => order.Cancel(4, " ", "SENTETIK", T0));
        Assert.Throws<ArgumentException>(() => order.Cancel(4, " padded", "SENTETIK", T0));
        Assert.Throws<ArgumentException>(() => order.Cancel(4, new string('a', PurchaseOrder.MaximumActorLength + 1), "SENTETIK", T0));
        Assert.Equal((PurchaseOrderStatus.Approved, 4L), (order.Status, order.Version));
    }

    [Fact]
    public void A_cancelled_order_takes_no_acceptance_share_and_no_breakdown_under_its_own_code()
    {
        var (order, kg, _) = Approved();
        order.Cancel(4, Canceller, "SENTETIK", T0);

        Assert.Equal(PurchasingRuleCodes.OrderCancelled, CodeOf(() => order.RecordAcceptance(5, Guid.NewGuid(), kg.Id, Kg(1m), T0)));
        Assert.Equal(PurchasingRuleCodes.OrderCancelled, CodeOf(() => order.BreakDown(5, kg.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), T0)));
        Assert.Equal(PurchasingRuleCodes.OrderNotDraft, CodeOf(() => order.Approve(5, Approver, T0)));
        Assert.Equal(5L, order.Version);
    }

    [Fact]
    public void The_store_rebuilds_a_cancelled_order_after_its_acceptances_and_refuses_a_row_whose_cancelled_share_disagrees()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var acceptanceId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);
        var row = new PurchaseOrderRow
        {
            Id = orderId, CompanyId = Company, SupplierPartyId = Supplier, CompanyAccountId = Guid.NewGuid(), Status = (int)PurchaseOrderStatus.Cancelled,
            Version = 5, EffectiveOn = new DateOnly(2026, 11, 3), CreatedAtUtc = at, UpdatedAtUtc = at, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
            CancelledBy = Canceller, CancelledAtUtc = at, CancellationReason = "SENTETIK",
        };
        var line = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 1, LineId = lineId, Kind = (int)PurchaseOrderLineKind.General, ProductId = Guid.NewGuid(),
            ProductVersion = "7", UnitId = KgUnit, UnitIsKilogram = true, Ordered = 10m, Technical = 0m, Accepted = 4m, Cancelled = 6m,
        };
        var acceptance = new PurchaseAcceptanceRow
        {
            AcceptanceId = acceptanceId, CompanyId = Company, OrderId = orderId, LineId = lineId, Quantity = 4m, OrderVersionAfter = 4, RecordedAtUtc = at,
            IdempotencyKeyHash = new string('A', 64), RequestHash = new string('B', 64), ActorId = "SENTETIK", CorrelationId = "SENTETIK", ResultJson = "{}",
        };

        var rebuilt = PurchasingStore.RestoreOrder(row, [line], [acceptance]);

        Assert.Equal((PurchaseOrderStatus.Cancelled, 5L, Canceller), (rebuilt.Status, rebuilt.Version, rebuilt.Cancellation!.CancelledBy));
        Assert.Equal((Kg(4m), Kg(6m), Kg(0m)), (rebuilt.Lines[0].AcceptedQuantity, rebuilt.Lines[0].CancelledQuantity, rebuilt.Lines[0].RemainingQuantity));

        line.Cancelled = 5m;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance]));
        line.Cancelled = 6m;
        row.CancelledBy = null;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance]));
    }
}
