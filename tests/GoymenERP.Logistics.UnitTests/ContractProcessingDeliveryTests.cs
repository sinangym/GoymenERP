using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// STK-S3: the subcontract ("fason teslim") shipment kind (LOJ-D1), its weighing order (LOJ-D12: weigh, then ship; no load
/// after the weighing) and the reasoned, append-only weighing correction after the vehicle left (LOJ-D13). SENTETIK.
/// </summary>
public sealed class ContractProcessingDeliveryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 30, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Operator = "SENTETIK-kantar";
    private const string Reason = "SENTETIK kantar fişi yanlış okunmuş";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (LoadingInstruction Instruction, LoadingInstructionLine Line) Issued(LoadingInstructionShipmentKind kind)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0, kind);
        var line = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(),
            KgUnit, null, Kg(10_000m), T0);
        instruction.Issue(2, T0);
        return (instruction, line);
    }

    /// <summary>Loaded 9.000 kg, weighed 9.050 kg net, shipped 9.000 kg: version 6 afterwards.</summary>
    private static (LoadingInstruction Instruction, LoadingInstructionLine Line, VehicleWeighing Weighing) Shipped()
    {
        var (instruction, line) = Issued(LoadingInstructionShipmentKind.ContractProcessingDelivery);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(9_000m), MeasurementKind.Measured, T0);
        var weighing = instruction.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(24_050m), Operator, T0);
        instruction.MarkShipped(5, Guid.NewGuid(), line.Id, Kg(9_000m), T0);
        return (instruction, line, weighing);
    }

    [Fact]
    public void An_instruction_is_a_sale_unless_created_as_a_subcontract_delivery_and_an_unknown_kind_is_refused()
    {
        var sale = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        var delivery = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0,
            LoadingInstructionShipmentKind.ContractProcessingDelivery);

        Assert.Equal((LoadingInstructionShipmentKind.Sale, false), (sale.ShipmentKind, sale.IsContractProcessingDelivery));
        Assert.Equal((LoadingInstructionShipmentKind.ContractProcessingDelivery, true),
            (delivery.ShipmentKind, delivery.IsContractProcessingDelivery));
        Assert.Equal(LogisticsRuleCodes.ShipmentKindRequired, Assert.Throws<LogisticsRuleException>(() =>
            LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0, (LoadingInstructionShipmentKind)3)).Code);
    }

    [Fact]
    public void A_subcontract_delivery_is_not_shipped_without_a_weighing_and_the_refusal_changes_nothing()
    {
        var (instruction, line) = Issued(LoadingInstructionShipmentKind.ContractProcessingDelivery);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(9_000m), MeasurementKind.Measured, T0);

        var refusal = Assert.Throws<LogisticsRuleException>(() => instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(9_000m), T0));

        Assert.Equal(LogisticsRuleCodes.ContractProcessingShipmentWithoutWeighing, refusal.Code);
        Assert.Equal((4L, 0m), (instruction.Version, line.ShippedQuantity.Value));
    }

    [Fact]
    public void A_sale_still_ships_without_a_weighing()
    {
        var (instruction, line) = Issued(LoadingInstructionShipmentKind.Sale);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(9_000m), MeasurementKind.Measured, T0);

        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(9_000m), T0);

        Assert.Equal(9_000m, line.ShippedQuantity.Value);
    }

    [Fact]
    public void A_subcontract_delivery_takes_no_load_after_its_weighing_but_a_sale_does()
    {
        var (delivery, deliveryLine) = Issued(LoadingInstructionShipmentKind.ContractProcessingDelivery);
        delivery.RecordLoad(3, Guid.NewGuid(), deliveryLine.Id, Kg(9_000m), MeasurementKind.Measured, T0);
        delivery.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(24_000m), Operator, T0);

        var refusal = Assert.Throws<LogisticsRuleException>(() =>
            delivery.RecordLoad(5, Guid.NewGuid(), deliveryLine.Id, Kg(500m), MeasurementKind.Measured, T0));

        Assert.Equal(LogisticsRuleCodes.ContractProcessingLoadAfterWeighing, refusal.Code);
        Assert.Equal((5L, 9_000m), (delivery.Version, deliveryLine.LoadedQuantity.Value));

        var (sale, saleLine) = Issued(LoadingInstructionShipmentKind.Sale);
        sale.RecordLoad(3, Guid.NewGuid(), saleLine.Id, Kg(9_000m), MeasurementKind.Measured, T0);
        sale.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(24_000m), Operator, T0);
        sale.RecordLoad(5, Guid.NewGuid(), saleLine.Id, Kg(500m), MeasurementKind.Measured, T0);
        Assert.Equal(9_500m, saleLine.LoadedQuantity.Value);
    }

    [Fact]
    public void A_correction_after_exit_is_appended_with_its_reason_and_keeps_the_original_record()
    {
        var (instruction, _, weighing) = Shipped();

        var correction = instruction.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(23_980m),
            $"  {Reason} ", $" {Operator} ", T0);

        Assert.Equal((2, 8_980m, 9_050m, -20m, Reason, Operator, 2, TimeSpan.Zero),
            (correction.Revision, correction.Net.Value, correction.PreviousNet.Value, correction.DifferenceKg, correction.Reason,
                correction.CorrectedBy, correction.SharesBefore, correction.CorrectedAtUtc.Offset));
        Assert.Same(weighing, instruction.Weighing);
        Assert.Equal(9_050m, instruction.Weighing!.Net.Value);
        Assert.Equal((2, 7L), (instruction.WeighingRevision, instruction.Version));

        var second = instruction.CorrectWeighing(7, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(23_990m), Kg(15_000m), Reason,
            Operator, T0);
        Assert.Equal((3, 8_990m, 8_980m), (second.Revision, second.Net.Value, second.PreviousNet.Value));
        Assert.Equal(2, instruction.WeighingCorrections.Count);
    }

    [Fact]
    public void A_correction_is_refused_before_any_weighing_or_shipment_and_without_a_bounded_reason()
    {
        var (unweighed, line) = Issued(LoadingInstructionShipmentKind.ContractProcessingDelivery);
        Assert.Equal(LogisticsRuleCodes.WeighingNotRecorded, Assert.Throws<LogisticsRuleException>(() =>
            unweighed.CorrectWeighing(3, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), Reason, Operator, T0)).Code);

        unweighed.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(9_000m), MeasurementKind.Measured, T0);
        unweighed.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(24_000m), Operator, T0);
        Assert.Equal(LogisticsRuleCodes.WeighingCorrectionBeforeShipment, Assert.Throws<LogisticsRuleException>(() =>
            unweighed.CorrectWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(23_000m), Reason, Operator, T0)).Code);

        var (shipped, _, _) = Shipped();
        foreach (var reason in new[] { "", "   ", new string('x', LoadingInstruction.WeighingCorrectionReasonMaximumLength + 1) })
        {
            Assert.Equal(LogisticsRuleCodes.WeighingCorrectionReasonRequired, Assert.Throws<LogisticsRuleException>(() =>
                shipped.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(23_000m), reason, Operator, T0)).Code);
        }

        Assert.Throws<ArgumentException>(() =>
            shipped.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(23_000m), Reason, " ", T0));
        Assert.Equal(LogisticsRuleCodes.WeighingDirectionInconsistent, Assert.Throws<LogisticsRuleException>(() =>
            shipped.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(23_000m), Kg(15_000m), Reason, Operator, T0)).Code);
        Assert.Throws<LogisticsVersionConflictException>(() =>
            shipped.CorrectWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(23_000m), Reason, Operator, T0));
        Assert.Equal((1, 6L, 0), (shipped.WeighingRevision, shipped.Version, shipped.WeighingCorrections.Count));
    }

    [Fact]
    public void A_correction_id_is_used_once()
    {
        var (instruction, _, weighing) = Shipped();
        var id = Guid.NewGuid();
        instruction.CorrectWeighing(6, id, WeighingDirection.TareFirst, Kg(15_000m), Kg(23_980m), Reason, Operator, T0);

        Assert.Equal(LogisticsRuleCodes.DuplicateWeighingCorrectionId, Assert.Throws<LogisticsRuleException>(() =>
            instruction.CorrectWeighing(7, id, WeighingDirection.TareFirst, Kg(15_000m), Kg(23_990m), Reason, Operator, T0)).Code);
        Assert.Equal(LogisticsRuleCodes.DuplicateWeighingCorrectionId, Assert.Throws<LogisticsRuleException>(() =>
            instruction.CorrectWeighing(7, weighing.WeighingId, WeighingDirection.TareFirst, Kg(15_000m), Kg(23_990m), Reason, Operator, T0)).Code);
    }

    [Fact]
    public void The_net_weight_read_states_the_last_correction_with_its_revision_and_never_splits()
    {
        var (instruction, _, weighing) = Shipped();
        var original = SubcontractDeliveryNetWeightReader.Read(Company, instruction.Id, weighing.WeighingId, instruction);
        Assert.Equal(("9050", LogisticsSubcontractDeliveryNetWeightContractV1.OriginalWeighingRevision),
            (original.Value!.NetKg, original.Value.WeighingRevision));

        instruction.CorrectWeighing(6, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(23_980m), Kg(15_000m), Reason, Operator, T0);
        var corrected = SubcontractDeliveryNetWeightReader.Read(Company, instruction.Id, weighing.WeighingId, instruction);

        Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.Available, corrected.Status);
        Assert.Equal(("8980", "GrossFirst", "-20", 2, "9000"),
            (corrected.Value!.NetKg, corrected.Value.Direction, corrected.Value.DifferenceKg, corrected.Value.WeighingRevision,
                corrected.Value.LoadedKgAtWeighing));
    }
}
