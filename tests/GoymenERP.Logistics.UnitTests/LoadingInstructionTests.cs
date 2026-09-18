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
    private static readonly Guid Allocation = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (LoadingInstruction Instruction, LoadingInstructionLine Line) Issued(decimal kilograms, Guid? allocationId = null)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), allocationId ?? Allocation, Kg(kilograms), T0);
        instruction.Issue(2, T0);
        return (instruction, line);
    }

    [Fact]
    public void An_instruction_belongs_to_one_company_and_one_customer_and_starts_as_a_draft()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, T0);
        Assert.Equal((Company, Customer, LoadingInstructionStatus.Draft, 1L),
            (instruction.CompanyId, instruction.CustomerPartyId, instruction.Status, instruction.Version));
        Assert.Equal(TimeSpan.Zero, instruction.CreatedAtUtc.Offset);
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.NewGuid(), Guid.Empty, Customer, T0));
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => LoadingInstruction.Create(Guid.Empty, Company, Customer, T0));
    }

    [Fact]
    public void Lines_are_added_only_while_draft_and_issuing_needs_a_line()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, T0);
        Assert.Throws<InvalidOperationException>(() => instruction.Issue(1, T0));
        var lineId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => instruction.AddLine(1, lineId, Guid.NewGuid(), null, Kg(0), T0));
        Assert.Throws<ArgumentException>(() => instruction.AddLine(1, lineId, Guid.NewGuid(), Guid.Empty, Kg(1), T0));
        var line = instruction.AddLine(1, lineId, Guid.NewGuid(), null, Kg(6_000m), T0);
        Assert.Null(line.AllocationId);
        Assert.Throws<InvalidOperationException>(() => instruction.AddLine(2, lineId, Guid.NewGuid(), null, Kg(1), T0));
        instruction.Issue(2, T0.AddMinutes(1));
        Assert.Equal(LoadingInstructionStatus.Issued, instruction.Status);
        Assert.Throws<InvalidOperationException>(() => instruction.AddLine(3, Guid.NewGuid(), Guid.NewGuid(), null, Kg(1), T0));
        Assert.Throws<InvalidOperationException>(() => instruction.Issue(3, T0));
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
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), Allocation, Kg(10m), T0);
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(2, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0));
        Assert.Throws<InvalidOperationException>(() => instruction.MarkShipped(2, Guid.NewGuid(), line.Id, Kg(1m), T0));
        Assert.Throws<InvalidOperationException>(() => instruction.MarkDelivered(2, Guid.NewGuid(), line.Id, Kg(1m), T0));
    }

    [Fact]
    public void Loading_four_of_six_tons_leaves_two_to_load_and_never_more_than_instructed()
    {
        var (instruction, line) = Issued(6_000m);
        var share = instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0.AddHours(1));

        Assert.Equal((Kg(4_000m), Kg(2_000m)), (line.LoadedQuantity, line.RemainingToLoad));
        Assert.Equal((LoadingShareKind.Loaded, (MeasurementKind?)MeasurementKind.Measured, TimeSpan.Zero),
            (share.Kind, share.Measurement, share.RecordedAtUtc.Offset));
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(4, Guid.NewGuid(), line.Id, Kg(2_000.001m), MeasurementKind.Measured, T0));
        Assert.Equal(Kg(4_000m), line.LoadedQuantity);
    }

    [Fact]
    public void Shipping_comes_out_of_the_loaded_share_only_so_the_loaded_sub_share_is_never_subtracted_twice()
    {
        // §13: 6.000 allocated, 4.000 loaded, 3.800 shipped → 200 loaded-but-not-shipped stays visible.
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_800m), T0);

        Assert.Equal(Kg(4_000m), line.LoadedQuantity);
        Assert.Equal(Kg(3_800m), line.ShippedQuantity);
        Assert.Equal(Kg(200m), line.LoadedNotShipped);
        Assert.Equal(Kg(2_000m), line.RemainingToLoad);
        Assert.Throws<InvalidOperationException>(() => instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(200.001m), T0));
        instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(200m), T0);
        Assert.True(line.LoadedNotShipped.IsZero);
        Assert.Equal(Kg(2_000m), line.RemainingToLoad);
    }

    [Fact]
    public void Delivery_never_exceeds_what_was_shipped()
    {
        var (instruction, line) = Issued(6_000m);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.AllocatedFromVehicleWeight, T0);
        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(3_800m), T0);
        Assert.Throws<InvalidOperationException>(() => instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, Kg(3_800.001m), T0));
        instruction.MarkDelivered(5, Guid.NewGuid(), line.Id, Kg(3_000m), T0);
        Assert.Equal((Kg(3_000m), Kg(800m)), (line.DeliveredQuantity, line.ShippedNotDelivered));
    }

    [Fact]
    public void Every_load_states_how_it_was_measured()
    {
        var (instruction, line) = Issued(6_000m);
        Assert.Throws<ArgumentException>(() =>
            instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1m), (MeasurementKind)0, T0));
        var share = instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.AllocatedFromVehicleWeight, T0);
        Assert.Equal((MeasurementKind?)MeasurementKind.AllocatedFromVehicleWeight, share.Measurement);
        var shipped = instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(1m), T0);
        Assert.Null(shipped.Measurement);
    }

    [Fact]
    public void A_blocked_line_loads_nothing_until_unblocked()
    {
        var (instruction, line) = Issued(6_000m);
        instruction.BlockLine(3, line.Id, T0);
        Assert.True(line.IsBlocked);
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(4, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0));
        instruction.UnblockLine(4, line.Id, T0);
        instruction.RecordLoad(5, Guid.NewGuid(), line.Id, Kg(1m), MeasurementKind.Measured, T0);
        Assert.Equal(Kg(1m), line.LoadedQuantity);
    }

    [Fact]
    public void The_same_share_id_is_one_share_across_loads_shipments_and_deliveries()
    {
        var (instruction, line) = Issued(6_000m);
        var id = Guid.NewGuid();
        instruction.RecordLoad(3, id, line.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(4, id, line.Id, Kg(1_000m), MeasurementKind.Measured, T0));
        Assert.Throws<InvalidOperationException>(() => instruction.MarkShipped(4, id, line.Id, Kg(1m), T0));
        Assert.Equal(Kg(1_000m), line.LoadedQuantity);
        Assert.Single(instruction.Shares);
    }

    [Fact]
    public void Shares_need_the_line_unit_a_positive_amount_a_known_line_and_the_current_version()
    {
        var (instruction, line) = Issued(6_000m);
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Quantity.Of(1m, QuantityUnit.Piece), MeasurementKind.Measured, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(0m), MeasurementKind.Measured, T0));
        Assert.Throws<InvalidOperationException>(() =>
            instruction.RecordLoad(3, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), MeasurementKind.Measured, T0));
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
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, T0);
        var pieces = instruction.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), null, Quantity.Of(20m, QuantityUnit.Piece), T0);
        var kilograms = instruction.AddLine(2, Guid.NewGuid(), Guid.NewGuid(), Allocation, Kg(500m), T0);
        instruction.Issue(3, T0);
        instruction.RecordLoad(4, Guid.NewGuid(), pieces.Id, Quantity.Of(20m, QuantityUnit.Piece), MeasurementKind.Measured, T0);
        Assert.True(pieces.RemainingToLoad.IsZero);
        Assert.Equal(Kg(500m), kilograms.RemainingToLoad);
        Assert.Equal(2, instruction.Lines.Count);
    }
}
