using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>#275 §7.4 / §13: an instruction is not a shipment; instructed, loaded, shipped and delivered are separate shares.</summary>
public sealed class LoadingInstructionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 11, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Customer = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Allocation = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<LogisticsRuleException>(action).Code;

    private static LoadingInstruction Draft() => LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);

    private static LoadingInstructionLine Line(LoadingInstruction instruction, long expectedVersion, decimal kilograms, Guid? allocationId = null) =>
        instruction.AddLine(expectedVersion, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), KgUnit, allocationId, Kg(kilograms), T0);

    private static (LoadingInstruction Instruction, LoadingInstructionLine Line) Issued(decimal kilograms)
    {
        var instruction = Draft();
        var line = Line(instruction, 1, kilograms, Allocation);
        instruction.Issue(2, T0);
        return (instruction, line);
    }

    [Fact]
    public void An_instruction_names_its_company_customer_party_and_company_account_and_starts_as_a_draft()
    {
        var instruction = Draft();
        Assert.Equal((Company, Customer, Account, LoadingInstructionStatus.Draft, 1L),
            (instruction.CompanyId, instruction.CustomerPartyId, instruction.CompanyAccountId, instruction.Status, instruction.Version));
        Assert.Equal(TimeSpan.Zero, instruction.CreatedAtUtc.Offset);
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.NewGuid(), Guid.Empty, Customer, Account, T0));
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.Empty, Account, T0));
        // A Party alone is not a customer of a company: the CompanyPartyAccount is required (§5).
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.Empty, Company, Customer, Account, T0));
    }

    [Fact]
    public void A_line_names_its_source_order_line_product_and_settings_unit()
    {
        var instruction = Draft();
        var orderLine = Guid.NewGuid();
        var product = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => instruction.AddLine(1, Guid.NewGuid(), Guid.Empty, product, KgUnit, null, Kg(1), T0));
        Assert.Throws<ArgumentException>(() => instruction.AddLine(1, Guid.NewGuid(), orderLine, Guid.Empty, KgUnit, null, Kg(1), T0));
        Assert.Throws<ArgumentException>(() => instruction.AddLine(1, Guid.NewGuid(), orderLine, product, Guid.Empty, null, Kg(1), T0));
        Assert.Throws<ArgumentException>(() => instruction.AddLine(1, Guid.NewGuid(), orderLine, product, KgUnit, Guid.Empty, Kg(1), T0));
        var line = instruction.AddLine(1, Guid.NewGuid(), orderLine, product, KgUnit, null, Kg(6_000m), T0);
        Assert.Equal((orderLine, product, KgUnit, (Guid?)null), (line.SourceOrderLineId, line.ProductId, line.UnitId, line.AllocationId));
    }

    [Fact]
    public void Lines_are_added_only_while_draft_and_issuing_needs_a_line()
    {
        var instruction = Draft();
        Assert.Equal(LogisticsRuleCodes.InstructionWithoutLines, CodeOf(() => instruction.Issue(1, T0)));
        var lineId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => instruction.AddLine(1, lineId, Guid.NewGuid(), Guid.NewGuid(), KgUnit, null, Kg(0), T0));
        instruction.AddLine(1, lineId, Guid.NewGuid(), Guid.NewGuid(), KgUnit, null, Kg(6_000m), T0);
        Assert.Equal(LogisticsRuleCodes.DuplicateLineId,
            CodeOf(() => instruction.AddLine(2, lineId, Guid.NewGuid(), Guid.NewGuid(), KgUnit, null, Kg(1), T0)));
        instruction.Issue(2, T0.AddMinutes(1));
        Assert.Equal(LoadingInstructionStatus.Issued, instruction.Status);
        Assert.Equal(LogisticsRuleCodes.InstructionNotDraft, CodeOf(() => Line(instruction, 3, 1)));
        Assert.Equal(LogisticsRuleCodes.InstructionNotDraft, CodeOf(() => instruction.Issue(3, T0)));
    }

    [Fact]
    public void An_issued_instruction_is_not_a_shipment_nothing_is_loaded_or_shipped_yet()
    {
        var (instruction, line) = Issued(6_000m);
        Assert.Equal((Kg(6_000m), Kg(0), Kg(0), Kg(0)),
            (line.InstructedQuantity, line.LoadedQuantity, line.ShippedQuantity, line.DeliveredQuantity));
        Assert.Equal(Kg(6_000m), line.RemainingToLoad);
        Assert.Empty(instruction.Shares);
    }

    [Fact]
    public void Nothing_is_recorded_against_a_draft()
    {
        var instruction = Draft();
        var line = Line(instruction, 1, 10m, Allocation);
        Assert.Equal(LogisticsRuleCodes.InstructionNotIssued,
            CodeOf(() => instruction.RecordLoad(2, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0)));
        Assert.Equal(LogisticsRuleCodes.InstructionNotIssued, CodeOf(() => instruction.MarkShipped(2, Guid.NewGuid(), line.Id, Kg(1m), T0)));
        Assert.Equal(LogisticsRuleCodes.InstructionNotIssued,
            CodeOf(() => instruction.MarkDelivered(2, Guid.NewGuid(), line.Id, Guid.NewGuid(), Kg(1m), T0)));
    }

    [Fact]
    public void Loading_four_of_six_tons_leaves_two_to_load_and_never_more_than_instructed()
    {
        var (instruction, line) = Issued(6_000m);
        var share = instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0.AddHours(1));

        Assert.Equal((Kg(4_000m), Kg(2_000m)), (line.LoadedQuantity, line.RemainingToLoad));
        Assert.Equal((LoadingShareKind.Loaded, (MeasurementKind?)MeasurementKind.Measured, TimeSpan.Zero),
            (share.Kind, share.Measurement, share.RecordedAtUtc.Offset));
        Assert.Equal(LogisticsRuleCodes.LoadExceedsInstructed,
            CodeOf(() => instruction.RecordLoad(4, Guid.NewGuid(), line.Id, Kg(2_000.001m), MeasurementKind.Measured, T0)));
        Assert.Equal(Kg(4_000m), line.LoadedQuantity);
    }

    [Fact]
    public void Shipping_comes_out_of_the_loaded_share_only_so_the_loaded_sub_share_is_never_subtracted_twice()
    {
        // §13: 6.000 allocated, 4.000 loaded, 3.800 shipped → 200 loaded-but-not-shipped stays visible.
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_800m), T0);

        Assert.Equal((Kg(4_000m), Kg(3_800m), Kg(200m), Kg(2_000m)),
            (line.LoadedQuantity, line.ShippedQuantity, line.LoadedNotShipped, line.RemainingToLoad));
        Assert.Equal(LogisticsRuleCodes.ShipmentExceedsLoaded,
            CodeOf(() => instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(200.001m), T0)));
        instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(200m), T0);
        Assert.True(line.LoadedNotShipped.IsZero);
        Assert.Equal(Kg(2_000m), line.RemainingToLoad);
    }

    [Fact]
    public void Delivery_names_its_shipment_and_never_exceeds_it()
    {
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.AllocatedFromVehicleWeight, T0);
        var shipment = instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_800m), T0);
        Assert.Equal(LogisticsRuleCodes.DeliveryExceedsShipment,
            CodeOf(() => instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, shipment.ShareId, Kg(3_800.001m), T0)));
        var delivery = instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, shipment.ShareId, Kg(3_000m), T0);
        Assert.Equal((Kg(3_000m), Kg(800m)), (line.DeliveredQuantity, line.ShippedNotDelivered));
        Assert.Equal((Guid?)shipment.ShareId, delivery.SourceShipmentId);
        Assert.Equal(Kg(800m), instruction.UndeliveredOf(shipment.ShareId));
    }

    [Fact]
    public void Deliveries_are_bound_to_their_source_shipment_and_capped_per_shipment()
    {
        // §7.4 "eksik/hasarlı/ret/iade kaynak sevke bağlı": S1 3.000 + S2 800 shipped; a delivery
        // never exceeds its own shipment's undelivered remainder even when the line still has more.
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        var s1 = instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_000m), T0);
        var s2 = instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(800m), T0);

        instruction.MarkDelivered(6, Guid.NewGuid(), line.Id, s1.ShareId, Kg(2_900m), T0);
        Assert.Equal((Kg(100m), Kg(900m)), (instruction.UndeliveredOf(s1.ShareId), line.ShippedNotDelivered));
        Assert.Equal(LogisticsRuleCodes.DeliveryExceedsShipment,
            CodeOf(() => instruction.MarkDelivered(7, Guid.NewGuid(), line.Id, s1.ShareId, Kg(200m), T0)));
        Assert.Equal(7, instruction.Version);

        instruction.MarkDelivered(7, Guid.NewGuid(), line.Id, s1.ShareId, Kg(100m), T0);
        instruction.MarkDelivered(8, Guid.NewGuid(), line.Id, s2.ShareId, Kg(800m), T0);
        Assert.True(instruction.UndeliveredOf(s1.ShareId).IsZero);
        Assert.True(instruction.UndeliveredOf(s2.ShareId).IsZero);
        Assert.True(line.ShippedNotDelivered.IsZero);
        Assert.Equal(new Guid?[] { s1.ShareId, s1.ShareId, s2.ShareId },
            instruction.Shares.Where(share => share.Kind == LoadingShareKind.Delivered).Select(share => share.SourceShipmentId));
    }

    [Fact]
    public void A_delivery_on_another_lines_shipment_is_refused_even_when_this_line_has_its_own_undelivered_share()
    {
        // ORG #282 P3-1: line b has a REAL undelivered shipment, so the line-level bound alone would
        // let 100 kg through; only the "shipment of another line" rule refuses it, and the code says so.
        var instruction = Draft();
        var a = Line(instruction, 1, 6_000m, Allocation);
        var b = Line(instruction, 2, 500m);
        instruction.Issue(3, T0);
        instruction.RecordLoad(4, Guid.NewGuid(), a.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        var shipmentA = instruction.MarkShipped(5, Guid.NewGuid(), a.Id, Kg(1_000m), T0);
        instruction.RecordLoad(6, Guid.NewGuid(), b.Id, Kg(500m), MeasurementKind.Measured, T0);
        var shipmentB = instruction.MarkShipped(7, Guid.NewGuid(), b.Id, Kg(500m), T0);
        Assert.Equal(Kg(500m), b.ShippedNotDelivered);

        Assert.Equal(LogisticsRuleCodes.ShipmentOfAnotherLine,
            CodeOf(() => instruction.MarkDelivered(8, Guid.NewGuid(), b.Id, shipmentA.ShareId, Kg(100m), T0)));
        Assert.True(b.DeliveredQuantity.IsZero);
        Assert.Equal(8, instruction.Version);

        instruction.MarkDelivered(8, Guid.NewGuid(), b.Id, shipmentB.ShareId, Kg(100m), T0);
        Assert.Equal(Kg(100m), b.DeliveredQuantity);
    }

    [Fact]
    public void A_delivery_needs_a_real_shipment_id()
    {
        var (instruction, line) = Issued(6_000m);
        var load = instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(1_000m), T0);
        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound,
            CodeOf(() => instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, Guid.NewGuid(), Kg(100m), T0)));
        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound,
            CodeOf(() => instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, load.ShareId, Kg(100m), T0)));
        Assert.Equal(LogisticsRuleCodes.ShipmentNotFound, CodeOf(() => instruction.UndeliveredOf(load.ShareId)));
        Assert.Equal(5, instruction.Version);
        Assert.Equal(2, instruction.Shares.Count);
    }

    [Fact]
    public void Every_load_states_how_it_was_measured()
    {
        var (instruction, line) = Issued(6_000m);
        Assert.Equal(LogisticsRuleCodes.MeasurementRequired,
            CodeOf(() => instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1m), (MeasurementKind)0, T0)));
        var share = instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.AllocatedFromVehicleWeight, T0);
        Assert.Equal((MeasurementKind?)MeasurementKind.AllocatedFromVehicleWeight, share.Measurement);
        var shipped = instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(1m), T0);
        Assert.Null(shipped.Measurement);
    }

    [Fact]
    public void A_blocked_line_loads_and_ships_nothing_until_unblocked()
    {
        // §7.4 "kalite bloku … engellenir", Ö3 "sevk serbestliği yok": the block matters most at the
        // moment goods would leave, so a line loaded before the block still ships nothing.
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.BlockLine(4, line.Id, T0);
        Assert.True(line.IsBlocked);
        Assert.Equal(LogisticsRuleCodes.LineBlocked,
            CodeOf(() => instruction.RecordLoad(5, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0)));
        Assert.Equal(LogisticsRuleCodes.LineBlocked, CodeOf(() => instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(1_000m), T0)));
        Assert.True(line.ShippedQuantity.IsZero);
        Assert.Equal(5, instruction.Version);
        Assert.Single(instruction.Shares);

        instruction.UnblockLine(5, line.Id, T0);
        instruction.MarkShipped(6, Guid.NewGuid(), line.Id, Kg(1_000m), T0);
        Assert.Equal(Kg(1_000m), line.ShippedQuantity);
    }

    [Fact]
    public void Delivery_of_goods_already_shipped_is_still_recorded_when_the_line_is_blocked_afterwards()
    {
        // Pinned behaviour, open question for Business (README): the goods have left; a delivery
        // record is a fact about the source shipment (§7.4), so a later block does not refuse it.
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        var shipment = instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_800m), T0);
        instruction.BlockLine(5, line.Id, T0);

        instruction.MarkDelivered(6, Guid.NewGuid(), line.Id, shipment.ShareId, Kg(3_000m), T0);

        Assert.Equal((Kg(3_000m), Kg(800m)), (line.DeliveredQuantity, line.ShippedNotDelivered));
        Assert.Equal(LogisticsRuleCodes.LineBlocked, CodeOf(() => instruction.MarkShipped(7, Guid.NewGuid(), line.Id, Kg(100m), T0)));
    }

    [Fact]
    public void The_same_share_id_is_one_share_across_loads_shipments_and_deliveries()
    {
        var (instruction, line) = Issued(6_000m);
        var id = Guid.NewGuid();
        instruction.RecordLoad(3, id, line.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        Assert.Equal(LogisticsRuleCodes.DuplicateShareId,
            CodeOf(() => instruction.RecordLoad(4, id, line.Id, Kg(1_000m), MeasurementKind.Measured, T0)));
        Assert.Equal(LogisticsRuleCodes.DuplicateShareId, CodeOf(() => instruction.MarkShipped(4, id, line.Id, Kg(1m), T0)));
        Assert.Equal(Kg(1_000m), line.LoadedQuantity);
        Assert.Single(instruction.Shares);
    }

    [Fact]
    public void Shares_need_the_line_unit_a_positive_amount_a_known_line_and_the_current_version()
    {
        var (instruction, line) = Issued(6_000m);
        Assert.Equal(LogisticsRuleCodes.UnitMismatch,
            CodeOf(() => instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Quantity.Of(1m, QuantityUnit.Piece), MeasurementKind.Measured, T0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(0m), MeasurementKind.Measured, T0));
        Assert.Equal(LogisticsRuleCodes.LineNotFound,
            CodeOf(() => instruction.RecordLoad(3, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), MeasurementKind.Measured, T0)));
        Assert.Throws<ArgumentException>(() =>
            instruction.RecordLoad(3, Guid.Empty, line.Id, Kg(1m), MeasurementKind.Measured, T0));
        var conflict = Assert.Throws<LogisticsVersionConflictException>(() =>
            instruction.RecordLoad(2, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0));
        Assert.Equal((2L, 3L), (conflict.ExpectedVersion, conflict.CurrentVersion));
        Assert.True(line.LoadedQuantity.IsZero);
        Assert.Empty(instruction.Shares);
    }

    [Fact]
    public void Pieces_and_kilograms_are_separate_lines_with_separate_shares()
    {
        var instruction = Draft();
        var pieces = instruction.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Quantity.Of(20m, QuantityUnit.Piece), T0);
        var kilograms = Line(instruction, 2, 500m, Allocation);
        instruction.Issue(3, T0);
        instruction.RecordLoad(4, Guid.NewGuid(), pieces.Id, Quantity.Of(20m, QuantityUnit.Piece), MeasurementKind.Measured, T0);
        Assert.True(pieces.RemainingToLoad.IsZero);
        Assert.Equal(Kg(500m), kilograms.RemainingToLoad);
        Assert.Equal(2, instruction.Lines.Count);
    }

    [Fact]
    public void The_views_cannot_be_used_to_bypass_the_rules()
    {
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        var before = instruction.Version;
        Assert.False(instruction.Lines is List<LoadingInstructionLine>);
        Assert.False(instruction.Shares is List<LoadingShare>);
        Assert.Throws<NotSupportedException>(() => ((IList<LoadingInstructionLine>)instruction.Lines).Add(line));
        Assert.Throws<NotSupportedException>(() => ((IList<LoadingShare>)instruction.Shares).Clear());
        Assert.Single(instruction.Lines);
        Assert.Single(instruction.Shares);
        // A failed cast is not a mutation: the version the record had before is the version it has after.
        Assert.Equal(before, instruction.Version);
    }

    [Fact]
    public void The_line_level_guard_is_a_second_authority_and_reports_itself_as_such()
    {
        var line = new LoadingInstructionLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), KgUnit, null, Kg(100m));
        line.AddLoaded(Kg(60m));
        Assert.Equal(LogisticsRuleCodes.InternalInvariant, CodeOf(() => line.AddLoaded(Kg(41m))));
        Assert.Equal(LogisticsRuleCodes.InternalInvariant, CodeOf(() => line.AddShipped(Kg(61m))));
        line.AddShipped(Kg(60m));
        Assert.Equal(LogisticsRuleCodes.InternalInvariant, CodeOf(() => line.AddDelivered(Kg(61m))));
        Assert.Equal((Kg(60m), Kg(60m), Kg(0m)), (line.LoadedQuantity, line.ShippedQuantity, line.DeliveredQuantity));
    }
}
