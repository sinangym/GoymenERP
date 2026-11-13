using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>F-020 (LOJ-D6..D8): cancelling an instruction keeps everything, marks who/when/why, and refuses once a shipment exists.</summary>
public sealed class LoadingInstructionCancelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset T1 = T0.AddHours(1);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Customer = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<LogisticsRuleException>(action).Code;

    private static (LoadingInstruction Instruction, LoadingInstructionLine Line) Draft()
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Customer, Account, T0);
        var line = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit,
            Guid.NewGuid(), Kg(6_000m), T0);
        return (instruction, line);
    }

    [Fact]
    public void A_draft_cancels_with_the_canceller_time_and_trimmed_reason_and_keeps_its_lines()
    {
        var (instruction, line) = Draft();
        instruction.CancelInstruction(2, " user-1 ", "  müşteri vazgeçti  ", T1);
        Assert.Equal((LoadingInstructionStatus.Cancelled, 3L, "user-1", "müşteri vazgeçti", T1.ToUniversalTime()),
            (instruction.Status, instruction.Version, instruction.CancelledBy, instruction.CancelReason, instruction.CancelledAtUtc));
        Assert.Single(instruction.Lines);
        Assert.Equal(Kg(6_000m), line.InstructedQuantity);
    }

    [Fact]
    public void An_issued_instruction_with_loaded_but_unshipped_quantity_cancels_and_keeps_the_load_share()
    {
        var (instruction, line) = Draft();
        instruction.Issue(2, T0);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.CancelInstruction(4, "user-1", "hata", T1);
        Assert.Equal(LoadingInstructionStatus.Cancelled, instruction.Status);
        Assert.Single(instruction.Shares);
        Assert.Equal(Kg(4_000m), line.LoadedQuantity);
    }

    [Fact]
    public void A_recorded_shipment_refuses_the_cancel_and_changes_nothing()
    {
        var (instruction, line) = Draft();
        instruction.Issue(2, T0);
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4_000m), MeasurementKind.Measured, T0);
        instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(1_000m), T0);
        Assert.Equal(LogisticsRuleCodes.InstructionShipped, CodeOf(() => instruction.CancelInstruction(5, "user-1", "geç", T1)));
        Assert.Equal((LoadingInstructionStatus.Issued, 5L, (string?)null), (instruction.Status, instruction.Version, instruction.CancelledBy));
    }

    [Fact]
    public void A_cancelled_instruction_cannot_be_cancelled_again_or_blocked_or_unblocked()
    {
        var (instruction, line) = Draft();
        instruction.CancelInstruction(2, "user-1", "hata", T1);
        Assert.Equal(LogisticsRuleCodes.InstructionCancelled, CodeOf(() => instruction.CancelInstruction(3, "user-2", "yine", T1)));
        Assert.Equal(LogisticsRuleCodes.InstructionCancelled, CodeOf(() => instruction.BlockLine(3, line.Id, T1)));
        Assert.Equal(LogisticsRuleCodes.InstructionCancelled, CodeOf(() => instruction.UnblockLine(3, line.Id, T1)));
        Assert.Equal("user-1", instruction.CancelledBy);
        Assert.Equal(3L, instruction.Version);
    }

    [Fact]
    public void The_cancel_needs_the_current_version_an_actor_and_a_bounded_reason()
    {
        var (instruction, _) = Draft();
        Assert.Throws<LogisticsVersionConflictException>(() => instruction.CancelInstruction(1, "user-1", "hata", T1));
        Assert.Throws<ArgumentException>(() => instruction.CancelInstruction(2, " ", "hata", T1));
        Assert.Throws<ArgumentException>(() => instruction.CancelInstruction(2, "user-1", "  ", T1));
        Assert.Throws<ArgumentException>(() => instruction.CancelInstruction(2, "user-1", new string('x', 501), T1));
        Assert.Equal((LoadingInstructionStatus.Draft, 2L), (instruction.Status, instruction.Version));
        instruction.CancelInstruction(2, "user-1", new string('x', 500), T1);
        Assert.Equal(LoadingInstructionStatus.Cancelled, instruction.Status);
    }
}
