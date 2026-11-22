using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AK-4 (KLN-D119, SAT-D434): a piece (adet) line loads and ships whole pieces — the commercial quantity — with the physical kg of
/// those pieces beside them; the weighbridge compares the vehicle against kilograms (a piece line's physical kg); the dispatch note
/// line carries the shipment's kg. A kg line takes no separate kg. Golden K02: 40 adet, 500,160 kg on the scale. SENTETIK.
/// </summary>
public sealed class LoadingInstructionPieceLineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static Quantity Pieces(decimal value) => Quantity.Of(value, QuantityUnit.Piece);

    private static (LoadingInstruction Instruction, LoadingInstructionLine Piece, LoadingInstructionLine Kilogram) Issued()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        var piece = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(),
            PieceUnit, null, Pieces(100), T0);
        var kilogram = instruction.AddLine(2, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(),
            KgUnit, null, Kg(1_000m), T0);
        instruction.Issue(3, T0);
        return (instruction, piece, kilogram);
    }

    [Fact]
    public void AK4_a_piece_line_loads_and_ships_pieces_with_their_physical_kg()
    {
        var (instruction, piece, _) = Issued();

        var load = instruction.RecordLoad(4, Guid.NewGuid(), piece.Id, Pieces(40), MeasurementKind.Measured, T0, Kg(495m));
        var shipment = instruction.MarkShipped(5, Guid.NewGuid(), piece.Id, Pieces(40), T0, Kg(495m));

        Assert.Equal((40m, QuantityUnit.Piece, 495m), (load.Quantity.Value, load.Quantity.Unit, load.Kilograms!.Value.Value));
        Assert.Equal((40m, 495m), (shipment.Quantity.Value, shipment.Kilograms!.Value.Value));
        Assert.Equal(40m, piece.ShippedQuantity.Value);
        Assert.Equal(495m, instruction.PhysicalKilograms(piece.Id, LoadingShareKind.Shipped).Value);
    }

    [Fact]
    public void AK4_a_piece_line_without_kg_and_a_kg_line_with_extra_kg_are_refused()
    {
        var (instruction, piece, kilogram) = Issued();

        Assert.Equal(LogisticsRuleCodes.PhysicalKilogramsRequired, Assert.Throws<LogisticsRuleException>(() =>
            instruction.RecordLoad(4, Guid.NewGuid(), piece.Id, Pieces(40), MeasurementKind.Measured, T0)).Code);
        Assert.Equal(LogisticsRuleCodes.PhysicalKilogramsOnKilogramLine, Assert.Throws<LogisticsRuleException>(() =>
            instruction.RecordLoad(4, Guid.NewGuid(), kilogram.Id, Kg(10m), MeasurementKind.Measured, T0, Kg(10m))).Code);
        Assert.Empty(instruction.Shares);
    }

    [Fact]
    public void AK4_a_piece_shipment_cannot_take_more_kg_than_was_loaded_and_not_shipped()
    {
        var (instruction, piece, _) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), piece.Id, Pieces(40), MeasurementKind.Measured, T0, Kg(495m));

        Assert.Equal(LogisticsRuleCodes.ShipmentExceedsLoaded, Assert.Throws<LogisticsRuleException>(() =>
            instruction.MarkShipped(5, Guid.NewGuid(), piece.Id, Pieces(40), T0, Kg(495.001m))).Code);
        Assert.Equal(0m, piece.ShippedQuantity.Value);
    }

    [Fact]
    public void AK4_the_weighbridge_compares_kg_lines_and_piece_lines_by_their_kilograms()
    {
        var (instruction, piece, kilogram) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), piece.Id, Pieces(40), MeasurementKind.Measured, T0, Kg(495m));
        instruction.RecordLoad(5, Guid.NewGuid(), kilogram.Id, Kg(500m), MeasurementKind.Measured, T0);

        var weighing = instruction.RecordWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(16_000.16m),
            "SENTETIK-kantar", T0);

        Assert.Equal((995m, 1000.16m, 5.16m), (weighing.LoadedAtWeighing.Value, weighing.Net.Value, weighing.DifferenceKg));
    }

    [Fact]
    public void AK4_the_dispatch_note_line_carries_the_piece_shipment_kg()
    {
        var (instruction, piece, kilogram) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), piece.Id, Pieces(40), MeasurementKind.Measured, T0, Kg(495m));
        instruction.RecordLoad(5, Guid.NewGuid(), kilogram.Id, Kg(500m), MeasurementKind.Measured, T0);
        var pieceShipment = instruction.MarkShipped(6, Guid.NewGuid(), piece.Id, Pieces(40), T0, Kg(495m));
        var kgShipment = instruction.MarkShipped(7, Guid.NewGuid(), kilogram.Id, Kg(500m), T0);

        var note = instruction.CreateDispatchNoteDraft(8, Guid.NewGuid(), [pieceShipment.ShareId, kgShipment.ShareId], "SENTETIK-sevk", T0);

        Assert.Equal([(40m, (decimal?)495m), (500m, null)],
            note.Lines.Select(line => (line.Quantity.Value, line.Kilograms?.Value)).ToArray());
    }
}
