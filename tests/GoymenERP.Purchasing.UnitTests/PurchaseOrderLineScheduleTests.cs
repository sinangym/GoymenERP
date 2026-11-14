using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21, master §7.3): a general purchase-order line's delivery schedule (termin dilimleri, hubvogal's "sıra")
/// is a whole set of quantity slices with delivery dates whose sum equals the line's ordered quantity exactly; it is open on a Draft
/// and an Approved order, refused on a cancelled one and on a technical line, replaces the previous set as a new order version, books
/// nothing and is rebuilt revision by revision through the domain. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderLineScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PieceUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";
    private const string Planner = "SENTETIK-planlayan";
    private static readonly DateOnly D1 = new(2026, 12, 1);
    private static readonly DateOnly D2 = new(2027, 1, 15);

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static Quantity Pieces(decimal value) => Quantity.Of(value, QuantityUnit.Piece);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static PurchaseOrderDeliverySliceInput Slice(Quantity quantity, DateOnly date) => new(Guid.NewGuid(), quantity, date);

    private static (PurchaseOrder Order, PurchaseOrderLine Line) Draft(decimal kg = 100m)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var line = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(kg), T0);
        return (order, line);
    }

    [Fact]
    public void A_draft_lines_schedule_is_recorded_in_the_given_order_numbered_from_one_and_books_nothing()
    {
        var (order, line) = Draft();
        var first = Slice(Kg(60m), D2);
        var second = Slice(Kg(40m), D1);

        var recorded = order.SetLineDeliverySchedule(2, line.Id, [first, second], Planner, T0.AddHours(1));

        Assert.Equal(3L, order.Version);
        Assert.Equal([(first.SliceId, 1, Kg(60m), D2), (second.SliceId, 2, Kg(40m), D1)],
            recorded.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
        Assert.Same(line.DeliverySchedule, recorded);
        Assert.Equal((Kg(100m), Kg(0m), Kg(0m), Kg(0m), Kg(100m)),
            (line.Quantity, line.TechnicalQuantity, line.AcceptedQuantity, line.CancelledQuantity, line.RemainingQuantity));
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void A_new_schedule_replaces_the_previous_set_whole_on_an_approved_order_and_after_a_partial_acceptance()
    {
        var (order, line) = Draft();
        var whole = Slice(Kg(100m), D1);
        order.SetLineDeliverySchedule(2, line.Id, [whole], Planner, T0);
        order.Approve(3, Approver, T0);
        order.RecordAcceptance(4, Guid.NewGuid(), line.Id, Kg(30m), T0);

        // ALS-D81: the received 30 kg stay on the same slice and date; only the unreceived 70 kg move to a new date.
        var replaced = order.SetLineDeliverySchedule(5, line.Id, [whole with { Quantity = Kg(30m) }, Slice(Kg(70m), D2)], Planner, T0);

        Assert.Equal((6L, 2), (order.Version, replaced.Count));
        Assert.Equal([Kg(30m), Kg(70m)], line.DeliverySchedule.Select(slice => slice.Quantity).ToArray());
        Assert.Equal((Kg(30m), Kg(70m)), (line.AcceptedQuantity, line.RemainingQuantity));
    }

    [Fact]
    public void A_cancelled_order_is_refused_under_its_own_code_and_nothing_moves()
    {
        var (order, line) = Draft();
        order.Approve(2, Approver, T0);
        order.Cancel(3, Planner, "SENTETIK", T0);

        Assert.Equal(PurchasingRuleCodes.OrderCancelled, CodeOf(() => order.SetLineDeliverySchedule(4, line.Id, [Slice(Kg(100m), D1)], Planner, T0)));
        Assert.Equal((4L, 0), (order.Version, line.DeliverySchedule.Count));
    }

    [Fact]
    public void A_technical_line_is_not_scheduled_on_its_own_and_its_general_lines_schedule_counts_the_ordered_quantity()
    {
        var (order, general) = Draft(1_000m);
        order.Approve(2, Approver, T0);
        var technical = order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);

        Assert.Equal(PurchasingRuleCodes.ScheduleOnTechnicalLine,
            CodeOf(() => order.SetLineDeliverySchedule(4, technical.Id, [Slice(Kg(75m), D1)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleTotalMismatch,
            CodeOf(() => order.SetLineDeliverySchedule(4, general.Id, [Slice(Kg(925m), D1)], Planner, T0)));
        order.SetLineDeliverySchedule(4, general.Id, [Slice(Kg(925m), D1), Slice(Kg(75m), D2)], Planner, T0);
        Assert.Equal(5L, order.Version);
    }

    [Fact]
    public void Every_slice_rule_is_refused_under_its_code_and_writes_nothing()
    {
        var (order, line) = Draft();
        var dup = Guid.NewGuid();
        Assert.Equal(PurchasingRuleCodes.ScheduleEmpty, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id, [], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleTooManySlices, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [.. Enumerable.Range(0, PurchaseOrder.MaximumScheduleSlices + 1).Select(_ => Slice(Kg(0.001m), D1))], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleDuplicateSliceId, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [new PurchaseOrderDeliverySliceInput(dup, Kg(50m), D1), new PurchaseOrderDeliverySliceInput(dup, Kg(50m), D2)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleSliceNotPositive, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [Slice(Kg(0m), D1), Slice(Kg(100m), D2)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.UnitMismatch, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [Slice(Pieces(100m), D1)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleTotalMismatch, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [Slice(Kg(99.999m), D1)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.ScheduleTotalMismatch, CodeOf(() => order.SetLineDeliverySchedule(2, line.Id,
            [Slice(Kg(60m), D1), Slice(Kg(40.001m), D2)], Planner, T0)));
        Assert.Equal(PurchasingRuleCodes.LineNotFound, CodeOf(() => order.SetLineDeliverySchedule(2, Guid.NewGuid(),
            [Slice(Kg(100m), D1)], Planner, T0)));
        Assert.Equal((2L, 0), (order.Version, line.DeliverySchedule.Count));
    }

    [Fact]
    public void The_bound_is_exactly_five_hundred_slices_and_a_piece_line_is_scheduled_in_pieces()
    {
        var (order, line) = Draft(0.5m);
        order.SetLineDeliverySchedule(2, line.Id,
            [.. Enumerable.Range(0, PurchaseOrder.MaximumScheduleSlices).Select(_ => Slice(Kg(0.001m), D1))], Planner, T0);
        Assert.Equal((PurchaseOrder.MaximumScheduleSlices, PurchaseOrder.MaximumScheduleSlices), (line.DeliverySchedule.Count,
            line.DeliverySchedule[^1].SliceNo));

        var pieces = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var pieceLine = pieces.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), PieceUnit, Pieces(40m), T0);
        pieces.SetLineDeliverySchedule(2, pieceLine.Id, [Slice(Pieces(15m), D1), Slice(Pieces(25m), D2)], Planner, T0);
        Assert.Equal(Pieces(25m), pieceLine.DeliverySchedule[1].Quantity);
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict_and_a_malformed_actor_slice_id_or_date_an_argument_error()
    {
        var (order, line) = Draft();
        Assert.Throws<PurchasingVersionConflictException>(() => order.SetLineDeliverySchedule(1, line.Id, [Slice(Kg(100m), D1)], Planner, T0));
        Assert.Throws<ArgumentException>(() => order.SetLineDeliverySchedule(2, line.Id, [Slice(Kg(100m), D1)], " ", T0));
        Assert.Throws<ArgumentException>(() => order.SetLineDeliverySchedule(2, line.Id,
            [new PurchaseOrderDeliverySliceInput(Guid.Empty, Kg(100m), D1)], Planner, T0));
        Assert.Throws<ArgumentException>(() => order.SetLineDeliverySchedule(2, line.Id, [Slice(Kg(100m), default)], Planner, T0));
        Assert.ThrowsAny<ArgumentException>(() => order.SetLineDeliverySchedule(2, line.Id, null!, Planner, T0));
        Assert.Equal((2L, 0), (order.Version, line.DeliverySchedule.Count));
    }

    [Fact]
    public void The_store_rebuilds_draft_and_approved_revisions_between_the_acceptances_and_refuses_a_revision_that_breaks_the_rules()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero);
        var row = new PurchaseOrderRow
        {
            Id = orderId, CompanyId = Company, SupplierPartyId = Supplier, CompanyAccountId = Guid.NewGuid(), Status = (int)PurchaseOrderStatus.Approved,
            Version = 6, EffectiveOn = new DateOnly(2026, 11, 12), CreatedAtUtc = at, UpdatedAtUtc = at, CreatedBy = Preparer, CorrelationId = "SENTETIK",
            ApprovedBy = Approver, ApprovedAtUtc = at,
        };
        var line = new PurchaseOrderLineRow
        {
            CompanyId = Company, OrderId = orderId, LineNo = 1, LineId = lineId, Kind = (int)PurchaseOrderLineKind.General, ProductId = Guid.NewGuid(),
            ProductVersion = "7", UnitId = KgUnit, UnitIsKilogram = true, Ordered = 10m, Technical = 0m, Accepted = 4m, Cancelled = 0m,
        };
        var acceptance = new PurchaseAcceptanceRow
        {
            AcceptanceId = Guid.NewGuid(), CompanyId = Company, OrderId = orderId, LineId = lineId, Quantity = 4m, OrderVersionAfter = 5, RecordedAtUtc = at,
            IdempotencyKeyHash = new string('A', 64), RequestHash = new string('B', 64), ActorId = "SENTETIK", CorrelationId = "SENTETIK", ResultJson = "{}",
        };
        PurchaseOrderLineDeliverySliceRow SliceRow(long version, int no, decimal quantity, DateOnly date, Guid? sliceId = null) => new()
        {
            CompanyId = Company, OrderId = orderId, LineId = lineId, OrderVersionAfter = version, SliceNo = no, SliceId = sliceId ?? Guid.NewGuid(),
            Quantity = quantity, DeliveryDate = date, RecordedBy = Planner, RecordedAtUtc = at, CorrelationId = "SENTETIK",
        };
        // v2: line; v3: Draft schedule; v4: approval; v5: acceptance; v6: the Approved revision.
        // ALS-D81: the Approved revision keeps the received 4 kg on the Draft's first slice (same id and date).
        var receivedSlice = Guid.NewGuid();
        var slices = new List<PurchaseOrderLineDeliverySliceRow>
        {
            SliceRow(6, 2, 6m, D2), SliceRow(3, 1, 10m, D1, receivedSlice), SliceRow(6, 1, 4m, D1, receivedSlice),
        };

        var rebuilt = PurchasingStore.RestoreOrder(row, [line], [acceptance], slices);

        Assert.Equal((PurchaseOrderStatus.Approved, 6L), (rebuilt.Status, rebuilt.Version));
        Assert.Equal([(1, Kg(4m), D1), (2, Kg(6m), D2)],
            rebuilt.Lines[0].DeliverySchedule.Select(slice => (slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
        Assert.Equal(Kg(4m), rebuilt.Lines[0].AcceptedQuantity);

        // A stored revision whose slices no longer sum to the ordered quantity is refused by the domain on rebuild.
        slices[0].Quantity = 5m;
        Assert.Equal(PurchasingRuleCodes.ScheduleTotalMismatch,
            Assert.Throws<PurchasingRuleException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], slices)).Code);
        slices[0].Quantity = 6m;

        // A gap in the slice numbers, or a revision off the version chain, is refused.
        slices[0].SliceNo = 3;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], slices));
        slices[0].SliceNo = 2;
        slices[1].OrderVersionAfter = 7;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], slices));
        slices[1].OrderVersionAfter = 3;

        // A stored revision after the acceptance that re-dates the received part is refused by the domain on rebuild (ALS-D81).
        slices[2].DeliveryDate = D2;
        Assert.Equal(PurchasingRuleCodes.ScheduleReceivedPortionLocked,
            Assert.Throws<PurchasingRuleException>(() => PurchasingStore.RestoreOrder(row, [line], [acceptance], slices)).Code);
    }

    [Fact]
    public void After_a_partial_acceptance_only_the_remaining_quantity_is_rescheduled_and_the_received_slices_are_locked()
    {
        // ALS-D81 (Sinan #66 6816723313 madde 3): 100 kg as 40 @D1 + 60 @D2, then 55 kg received — the share covers the first
        // slice whole and 15 kg of the second (in slice order).
        var (order, line) = Draft();
        var first = Slice(Kg(40m), D1);
        var second = Slice(Kg(60m), D2);
        order.SetLineDeliverySchedule(2, line.Id, [first, second], Planner, T0);
        order.Approve(3, Approver, T0);
        order.RecordAcceptance(4, Guid.NewGuid(), line.Id, Kg(55m), T0);
        var later = new DateOnly(2027, 3, 1);

        foreach (var refused in new PurchaseOrderDeliverySliceInput[][]
                 {
                     [first with { DeliveryDate = later }, second],                                   // a whole-received slice re-dated
                     [first with { Quantity = Kg(30m) }, second with { Quantity = Kg(70m) }],         // a whole-received slice shrunk
                     [Slice(Kg(40m), D1), second],                                                   // a whole-received slice renamed
                     [second, first],                                                                // received slices reordered
                     [first, second with { DeliveryDate = later }],                                  // the part-received slice re-dated
                     [first, second with { Quantity = Kg(10m) }, Slice(Kg(50m), later)],             // shrunk below its received 15 kg
                     [Slice(Kg(100m), later)],                                                       // the whole line re-planned
                 })
        {
            Assert.Equal(PurchasingRuleCodes.ScheduleReceivedPortionLocked,
                CodeOf(() => order.SetLineDeliverySchedule(5, line.Id, refused, Planner, T0)));
            Assert.Equal(5L, order.Version);
            Assert.Equal([first.SliceId, second.SliceId], line.DeliverySchedule.Select(slice => slice.SliceId).ToArray());
        }

        // The unreceived 45 kg of the part-received slice move freely: split off to two later dates, the received 15 kg stay on D2.
        var rest = Slice(Kg(25m), later);
        var last = Slice(Kg(20m), later.AddDays(10));
        order.SetLineDeliverySchedule(5, line.Id, [first, second with { Quantity = Kg(15m) }, rest, last], Planner, T0);
        Assert.Equal([(first.SliceId, Kg(40m), D1), (second.SliceId, Kg(15m), D2), (rest.SliceId, Kg(25m), later), (last.SliceId, Kg(20m), later.AddDays(10))],
            line.DeliverySchedule.Select(slice => (slice.SliceId, slice.Quantity, slice.DeliveryDate)).ToArray());

        // The part-received slice may also grow again on its own date, and every unreceived slice is free.
        order.SetLineDeliverySchedule(6, line.Id, [first, second with { Quantity = Kg(60m) }], Planner, T0);
        Assert.Equal((7L, Kg(55m), Kg(45m)), (order.Version, line.AcceptedQuantity, line.RemainingQuantity));
    }

    [Fact]
    public void Without_an_acceptance_or_without_an_earlier_schedule_nothing_is_locked()
    {
        var (order, line) = Draft();
        order.Approve(2, Approver, T0);
        order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Kg(30m), T0);

        // No earlier schedule: the received part has no termin to lock.
        order.SetLineDeliverySchedule(4, line.Id, [Slice(Kg(100m), D2)], Planner, T0);
        Assert.Equal(5L, order.Version);

        var (draft, draftLine) = Draft();
        draft.SetLineDeliverySchedule(2, draftLine.Id, [Slice(Kg(100m), D1)], Planner, T0);
        draft.SetLineDeliverySchedule(3, draftLine.Id, [Slice(Kg(50m), D2), Slice(Kg(50m), D2)], Planner, T0);
        Assert.Equal(4L, draft.Version);
    }
}
