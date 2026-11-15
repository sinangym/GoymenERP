using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// LOJ-D17: after a cancel the loaded goods are taken off the vehicle and weighed by hand; the difference to the kilograms loaded
/// at the cancel is a warning, never a refusal; a cancelled instruction with loaded goods and no unload weighing is marked
/// "indirme kaydı yok". LOJ-D16: the cancel never releases the order line allocation the instruction line was cut from. SENTETIK.
/// </summary>
public sealed class UnloadWeighingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly DateTimeOffset T2 = T0.AddHours(2);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Customer = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Operator = "SENTETIK-kantar";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<LogisticsRuleException>(action).Code;

    /// <summary>An issued instruction with 4 000 kg loaded, cancelled at version 4 → version 5.</summary>
    private static (LoadingInstruction Instruction, LoadingInstructionLine Line, Guid AllocationId) CancelledAfterLoad()
    {
        var allocationId = Guid.NewGuid();
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit,
            allocationId, Kg(6_000m), T0);
        instruction.Issue(2, T0);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.CancelInstruction(4, "user-1", "hata", T1);
        return (instruction, line, allocationId);
    }

    [Fact]
    public void A_cancelled_instruction_with_loaded_goods_and_no_unload_weighing_is_marked_missing()
    {
        var (instruction, _, _) = CancelledAfterLoad();
        Assert.Equal(UnloadRecordState.Missing, instruction.UnloadRecord);
        Assert.Null(instruction.UnloadWeighing);
        Assert.Equal(LoadingInstructionStatus.Cancelled, instruction.Status);
    }

    [Fact]
    public void An_issued_or_draft_instruction_or_a_cancel_with_nothing_loaded_has_nothing_to_unload()
    {
        var draft = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        var line = draft.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null,
            Kg(6_000m), T0);
        Assert.Equal(UnloadRecordState.NotApplicable, draft.UnloadRecord);
        draft.Issue(2, T0);
        draft.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(1_000m), MeasurementKind.Measured, T0);
        Assert.Equal(UnloadRecordState.NotApplicable, draft.UnloadRecord);

        var emptyCancel = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        emptyCancel.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6_000m), T0);
        emptyCancel.CancelInstruction(2, "user-1", "hata", T1);
        Assert.Equal(UnloadRecordState.NotApplicable, emptyCancel.UnloadRecord);
    }

    [Fact]
    public void The_unload_weighing_records_the_net_the_loaded_kilograms_at_cancel_and_a_warning_difference()
    {
        var (instruction, _, _) = CancelledAfterLoad();
        var weighingId = Guid.NewGuid();
        var unload = instruction.RecordUnloadWeighing(5, weighingId, WeighingDirection.GrossFirst, Kg(19_050m), Kg(15_000m), $" {Operator} ", T2);

        Assert.Equal((weighingId, WeighingDirection.GrossFirst, Kg(4_050m), Kg(4_000m), 50m, Operator, T2.ToUniversalTime()),
            (unload.WeighingId, unload.Direction, unload.Net, unload.LoadedAtCancel, unload.DifferenceKg, unload.RecordedBy, unload.RecordedAtUtc));
        Assert.Equal((Kg(15_000m), Kg(19_050m)), (unload.Tare, unload.Gross));
        Assert.True(unload.DifferenceWarning);
        Assert.Same(unload, instruction.UnloadWeighing);
        Assert.Equal(UnloadRecordState.Recorded, instruction.UnloadRecord);
        Assert.Equal((LoadingInstructionStatus.Cancelled, 6L), (instruction.Status, instruction.Version));
    }

    [Theory]
    [InlineData("15000", "18990", "-10")]
    [InlineData("15000", "19000", "0")]
    public void A_lighter_or_equal_unload_is_signed_and_only_a_non_zero_difference_warns(string tare, string gross, string difference)
    {
        var (instruction, _, _) = CancelledAfterLoad();
        var unload = instruction.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst,
            Kg(decimal.Parse(tare, System.Globalization.CultureInfo.InvariantCulture)),
            Kg(decimal.Parse(gross, System.Globalization.CultureInfo.InvariantCulture)), Operator, T2);
        var expected = decimal.Parse(difference, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, unload.DifferenceKg);
        Assert.Equal(expected != 0m, unload.DifferenceWarning);
    }

    [Fact]
    public void The_unload_weighing_never_changes_the_loaded_quantity_the_shares_or_the_cancel_marks()
    {
        var (instruction, line, _) = CancelledAfterLoad();
        var sharesBefore = instruction.Shares.Count;
        instruction.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(18_000m), Operator, T2);
        Assert.Equal(Kg(4_000m), line.LoadedQuantity);
        Assert.Equal(sharesBefore, instruction.Shares.Count);
        Assert.Null(instruction.Weighing);
        Assert.Equal(("user-1", "hata", T1.ToUniversalTime()), (instruction.CancelledBy, instruction.CancelReason, instruction.CancelledAtUtc));
    }

    [Fact]
    public void An_instruction_that_is_not_cancelled_refuses_the_unload_weighing()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null,
            Kg(6_000m), T0);
        instruction.Issue(2, T0);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        Assert.Equal(LogisticsRuleCodes.InstructionNotCancelled,
            CodeOf(() => instruction.RecordUnloadWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2)));
        Assert.Null(instruction.UnloadWeighing);
        Assert.Equal(4L, instruction.Version);
    }

    [Fact]
    public void A_second_unload_weighing_is_refused_and_the_first_stays()
    {
        var (instruction, _, _) = CancelledAfterLoad();
        var first = instruction.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2);
        Assert.Equal(LogisticsRuleCodes.UnloadWeighingAlreadyRecorded,
            CodeOf(() => instruction.RecordUnloadWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_500m), Operator, T2)));
        Assert.Same(first, instruction.UnloadWeighing);
        Assert.Equal(6L, instruction.Version);
    }

    [Fact]
    public void A_cancel_with_nothing_loaded_refuses_the_unload_weighing()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6_000m), T0);
        instruction.CancelInstruction(2, "user-1", "hata", T1);
        Assert.Equal(LogisticsRuleCodes.UnloadNothingLoaded,
            CodeOf(() => instruction.RecordUnloadWeighing(3, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2)));
        Assert.Equal(UnloadRecordState.NotApplicable, instruction.UnloadRecord);
    }

    [Fact]
    public void A_line_not_in_kilograms_refuses_the_unload_weighing()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null,
            Kg(6_000m), T0);
        instruction.AddLine(2, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), Guid.NewGuid(), null,
            Quantity.Of(10, QuantityUnit.Piece), T0);
        instruction.Issue(3, T0);
        instruction.RecordLoad(4, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.CancelInstruction(5, "user-1", "hata", T1);
        Assert.Equal(LogisticsRuleCodes.WeighingUnitUnsupported,
            CodeOf(() => instruction.RecordUnloadWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2)));
    }

    [Fact]
    public void The_unload_weighing_needs_the_current_version_an_id_an_actor_and_a_consistent_direction()
    {
        var (instruction, _, _) = CancelledAfterLoad();
        Assert.Throws<LogisticsVersionConflictException>(() =>
            instruction.RecordUnloadWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2));
        Assert.Throws<ArgumentException>(() =>
            instruction.RecordUnloadWeighing(5, Guid.Empty, WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), Operator, T2));
        Assert.Throws<ArgumentException>(() =>
            instruction.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(19_000m), " ", T2));
        Assert.Equal(LogisticsRuleCodes.WeighingDirectionInconsistent,
            CodeOf(() => instruction.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(19_000m), Kg(15_000m), Operator, T2)));
        Assert.Null(instruction.UnloadWeighing);
        Assert.Equal(5L, instruction.Version);
    }

    [Fact]
    public void Loj_d16_the_cancel_keeps_the_order_line_allocation_on_the_instruction_line()
    {
        var (instruction, line, allocationId) = CancelledAfterLoad();
        Assert.Equal(allocationId, line.AllocationId);
        Assert.Equal(Kg(6_000m), line.InstructedQuantity);
        Assert.Equal(allocationId, Assert.Single(instruction.Lines).AllocationId);
    }
}
