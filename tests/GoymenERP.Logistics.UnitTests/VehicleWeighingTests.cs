using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-053 (master v3 §28.12 WGT-001 / WGT-002): the net of a manual weighbridge record is |second − first| with the
/// direction naming the tare, it is consistent with that direction, and the difference to the loaded kilograms is a
/// signed fact with no tolerance verdict (OPEN-004). One record per instruction (its correction is not decided).
/// SENTETIK.
/// </summary>
public sealed class VehicleWeighingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 9, 30, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Operator = "SENTETIK-kantar";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (LoadingInstruction Instruction, LoadingInstructionLine First, LoadingInstructionLine Second) Issued()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        var first = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(20_000m), T0);
        var second = instruction.AddLine(2, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6_000m), T0);
        instruction.Issue(3, T0);
        return (instruction, first, second);
    }

    [Theory]
    [InlineData(WeighingDirection.TareFirst, "15000", "39120.5", "24120.5")]
    [InlineData(WeighingDirection.GrossFirst, "39120.5", "15000", "24120.5")]
    [InlineData(WeighingDirection.TareFirst, "0.001", "0.002", "0.001")]
    public void The_net_is_the_absolute_difference_of_the_two_weighings_with_the_direction_naming_the_tare(
        WeighingDirection direction, string first, string second, string net)
    {
        var result = WeighbridgeCalculator.Net(direction, Kg(decimal.Parse(first, System.Globalization.CultureInfo.InvariantCulture)),
            Kg(decimal.Parse(second, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(net, result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(WeighingDirection.TareFirst, 39120, 15000)]
    [InlineData(WeighingDirection.GrossFirst, 15000, 39120)]
    [InlineData(WeighingDirection.TareFirst, 15000, 15000)]
    [InlineData(WeighingDirection.GrossFirst, 15000, 15000)]
    public void A_pair_that_contradicts_the_declared_direction_is_refused_never_turned_around(WeighingDirection direction, int first, int second)
    {
        var refusal = Assert.Throws<LogisticsRuleException>(() => WeighbridgeCalculator.Net(direction, Kg(first), Kg(second)));

        Assert.Equal(LogisticsRuleCodes.WeighingDirectionInconsistent, refusal.Code);
    }

    [Fact]
    public void The_calculator_takes_kilograms_a_known_direction_and_positive_weighings_only()
    {
        Assert.Equal(LogisticsRuleCodes.WeighingDirectionRequired,
            Assert.Throws<LogisticsRuleException>(() => WeighbridgeCalculator.Net((WeighingDirection)3, Kg(1), Kg(2))).Code);
        Assert.Equal(LogisticsRuleCodes.WeighingUnitUnsupported,
            Assert.Throws<LogisticsRuleException>(() => WeighbridgeCalculator.Net(WeighingDirection.TareFirst, Kg(1),
                Quantity.Of(2, QuantityUnit.Piece))).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => WeighbridgeCalculator.Net(WeighingDirection.TareFirst, Kg(0), Kg(2)));
        Assert.Equal(-120m, WeighbridgeCalculator.Difference(Kg(23_880m), Kg(24_000m)));
        Assert.Equal("0", WeighbridgeCalculator.Difference(Kg(10.5m), Kg(10.5m)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2", WeighbridgeCalculator.Difference(Kg(12.5m), Kg(10.5m)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_weighing_keeps_the_loaded_kilograms_of_every_line_and_the_signed_difference_as_facts_and_moves_no_share()
    {
        var (instruction, first, second) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(18_000.5m), MeasurementKind.Measured, T0);
        instruction.RecordLoad(5, Guid.NewGuid(), second.Id, Kg(5_999.5m), MeasurementKind.AllocatedFromVehicleWeight, T0);

        var weighing = instruction.RecordWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(38_880m), $"  {Operator} ", T0);

        Assert.Equal((23_880m, 24_000m, -120m, 2, Operator, TimeSpan.Zero),
            (weighing.Net.Value, weighing.LoadedAtWeighing.Value, weighing.DifferenceKg, weighing.SharesBefore, weighing.RecordedBy, weighing.RecordedAtUtc.Offset));
        Assert.Equal("24000", weighing.LoadedAtWeighing.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((15_000m, 38_880m), (weighing.Tare.Value, weighing.Gross.Value));
        Assert.Same(weighing, instruction.Weighing);
        Assert.Equal(7, instruction.Version);
        // Not a share and not a verdict: loads, shipments and deliveries are untouched; a load after it is still accepted.
        Assert.Equal(2, instruction.Shares.Count);
        Assert.Equal((18_000.5m, 5_999.5m), (first.LoadedQuantity.Value, second.LoadedQuantity.Value));
        instruction.RecordLoad(7, Guid.NewGuid(), first.Id, Kg(1m), MeasurementKind.Measured, T0);
        Assert.Equal(24_000m, instruction.Weighing!.LoadedAtWeighing.Value);
    }

    [Fact]
    public void A_heavier_weighing_than_loaded_is_a_positive_difference_and_nothing_is_judged()
    {
        var (instruction, first, _) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(10_000m), MeasurementKind.Measured, T0);

        var weighing = instruction.RecordWeighing(5, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(25_500.25m), Kg(15_000m), Operator, T0);

        Assert.Equal((10_500.25m, 10_000m, 500.25m), (weighing.Net.Value, weighing.LoadedAtWeighing.Value, weighing.DifferenceKg));
    }

    [Fact]
    public void Only_an_issued_instruction_at_the_expected_version_takes_one_weighing_and_a_second_is_refused_not_replaced()
    {
        var draft = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
        draft.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(1m), T0);
        Assert.Equal(LogisticsRuleCodes.InstructionNotIssued, Assert.Throws<LogisticsRuleException>(() =>
            draft.RecordWeighing(2, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), Operator, T0)).Code);

        var (instruction, _, _) = Issued();
        Assert.Throws<LogisticsVersionConflictException>(() =>
            instruction.RecordWeighing(3, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), Operator, T0));
        Assert.Throws<ArgumentException>(() => instruction.RecordWeighing(4, Guid.Empty, WeighingDirection.TareFirst, Kg(1m), Kg(2m), Operator, T0));
        Assert.Throws<ArgumentException>(() => instruction.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), " ", T0));
        Assert.Equal(LogisticsRuleCodes.WeighingDirectionInconsistent, Assert.Throws<LogisticsRuleException>(() =>
            instruction.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(2m), Kg(1m), Operator, T0)).Code);
        Assert.Null(instruction.Weighing);
        Assert.Equal(4, instruction.Version);

        var recorded = instruction.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), Operator, T0);
        Assert.Equal(LogisticsRuleCodes.WeighingAlreadyRecorded, Assert.Throws<LogisticsRuleException>(() =>
            instruction.RecordWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(3m), Operator, T0)).Code);
        Assert.Same(recorded, instruction.Weighing);
        Assert.Equal(5, instruction.Version);
    }
}
