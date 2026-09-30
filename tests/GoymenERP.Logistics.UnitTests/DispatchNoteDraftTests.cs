using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-055 in the aggregate: a dispatch-note draft is made from definite shipments of the instruction only, its lines are those
/// shipments' shipped quantities in recording order, and a shipment is linked to at most one dispatch note. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DispatchNoteDraftTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 11, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<LogisticsRuleException>(action).Code;

    /// <summary>Two lines; line A loaded 6 and shipped 4 then 1.5; line B loaded 2 and shipped 2. Version 10.</summary>
    private static (LoadingInstruction Instruction, Guid LineA, Guid LineB, Guid[] Shipments, Guid Load) Shipped()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        var a = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(10), T0);
        var b = instruction.AddLine(2, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(5), T0);
        instruction.Issue(3, T0);
        var load = Guid.NewGuid();
        instruction.RecordLoad(4, load, a.Id, Kg(6), MeasurementKind.Measured, T0);
        instruction.RecordLoad(5, Guid.NewGuid(), b.Id, Kg(2), MeasurementKind.Measured, T0);
        Guid[] shipments = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        instruction.MarkShipped(6, shipments[0], a.Id, Kg(4), T0);
        instruction.MarkShipped(7, shipments[1], b.Id, Kg(2), T0);
        instruction.MarkShipped(8, shipments[2], a.Id, Kg(1.5m), T0);
        instruction.MarkDelivered(9, Guid.NewGuid(), a.Id, shipments[0], Kg(1), T0);
        return (instruction, a.Id, b.Id, shipments, load);
    }

    [Fact]
    public void A_draft_takes_each_named_shipments_shipped_quantity_in_recording_order_and_moves_the_version_once()
    {
        var (instruction, lineA, lineB, shipments, _) = Shipped();
        var id = Guid.NewGuid();

        // Named out of order: the lines follow the recording order of the shipments.
        var draft = instruction.CreateDispatchNoteDraft(10, id, [shipments[2], shipments[0], shipments[1]], "  synthetic-actor ", T0);

        Assert.Equal((id, DispatchNoteStatus.Draft, "synthetic-actor", 6, TimeSpan.Zero),
            (draft.DispatchNoteId, draft.Status, draft.CreatedBy, draft.SharesBefore, draft.CreatedAtUtc.Offset));
        Assert.Equal([(1, shipments[0], lineA, 4m), (2, shipments[1], lineB, 2m), (3, shipments[2], lineA, 1.5m)],
            draft.Lines.Select(line => (line.LineNo, line.ShipmentId, line.InstructionLineId, line.Quantity.Value)).ToArray());
        Assert.Equal(11, instruction.Version);
        Assert.Same(draft, Assert.Single(instruction.DispatchNotes));
        // The shipped quantity, not the delivered one or the remainder: shipment 0 had 1 kg delivered and still shows 4.
        Assert.Equal(id, instruction.DispatchNoteOf(shipments[0]));
        // A document books nothing: the line totals stay.
        Assert.Equal((6m, 5.5m, 1m), (instruction.Lines[0].LoadedQuantity.Value, instruction.Lines[0].ShippedQuantity.Value,
            instruction.Lines[0].DeliveredQuantity.Value));
        Assert.Throws<NotSupportedException>(() => ((IList<DispatchNoteLine>)draft.Lines).Add(draft.Lines[0]));
    }

    [Fact]
    public void A_shipment_is_linked_to_at_most_one_dispatch_note_and_a_refused_draft_changes_nothing()
    {
        var (instruction, _, _, shipments, _) = Shipped();
        instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [shipments[0]], "synthetic-actor", T0);

        Assert.Equal(LogisticsRuleCodes.ShipmentAlreadyDispatched,
            CodeOf(() => instruction.CreateDispatchNoteDraft(11, Guid.NewGuid(), [shipments[1], shipments[0]], "synthetic-actor", T0)));
        Assert.Equal((11L, 1), (instruction.Version, instruction.DispatchNotes.Count));
        Assert.Null(instruction.DispatchNoteOf(shipments[1]));

        instruction.CreateDispatchNoteDraft(11, Guid.NewGuid(), [shipments[1], shipments[2]], "synthetic-actor", T0);
        Assert.Equal(2, instruction.DispatchNotes.Count);
    }

    [Fact]
    public void Only_recorded_shipments_of_an_issued_instruction_at_the_current_version_with_a_fresh_id_make_a_draft()
    {
        var (instruction, _, _, shipments, load) = Shipped();

        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound, CodeOf(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [Guid.NewGuid()], "a", T0)));
        // A load share and a delivery share are not definite shipments.
        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound, CodeOf(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [load], "a", T0)));
        var delivery = instruction.Shares.Single(share => share.Kind == LoadingShareKind.Delivered).ShareId;
        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound, CodeOf(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [delivery], "a", T0)));
        Assert.Throws<LogisticsVersionConflictException>(() => instruction.CreateDispatchNoteDraft(9, Guid.NewGuid(), [shipments[0]], "a", T0));
        Assert.Throws<ArgumentException>(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [], "a", T0));
        Assert.Throws<ArgumentException>(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [shipments[0], shipments[0]], "a", T0));
        Assert.Throws<ArgumentException>(() => instruction.CreateDispatchNoteDraft(10, Guid.Empty, [shipments[0]], "a", T0));
        Assert.Throws<ArgumentException>(() => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(), [shipments[0]], " ", T0));
        Assert.Equal((10L, 0), (instruction.Version, instruction.DispatchNotes.Count));

        var id = Guid.NewGuid();
        instruction.CreateDispatchNoteDraft(10, id, [shipments[0]], "a", T0);
        Assert.Equal(LogisticsRuleCodes.DuplicateDispatchNoteId, CodeOf(() => instruction.CreateDispatchNoteDraft(11, id, [shipments[1]], "a", T0)));

        var draft = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        Assert.Equal(LogisticsRuleCodes.InstructionNotIssued, CodeOf(() => draft.CreateDispatchNoteDraft(1, Guid.NewGuid(), [shipments[0]], "a", T0)));
    }

    [Fact]
    public void A_block_after_the_shipment_does_not_stop_its_dispatch_note()
    {
        var (instruction, lineA, _, shipments, _) = Shipped();
        instruction.BlockLine(10, lineA, T0);

        var draft = instruction.CreateDispatchNoteDraft(11, Guid.NewGuid(), [shipments[0]], "a", T0);

        Assert.Equal(4m, Assert.Single(draft.Lines).Quantity.Value);
    }
}
