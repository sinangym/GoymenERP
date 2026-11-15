using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D53 (SINAN_KARARLARI_2026-11-14_URETIM_EK4): an over-weighed output is corrected through a proposal and a second
/// person's approval. The wrong weighing is not deleted: the approval records the reversal of its full net mass and the
/// correct weighing as a new declaration on the same roll, class and plan output line, and the correction keeps the
/// trace. The proposer can neither approve nor reject; only a downward correction of a weighed output on an open order
/// is accepted (a closed order waits for URT-D52 (3)); a refused approval changes nothing.
/// </summary>
public sealed class WeighingCorrectionTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-0000000d5301");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-0000000d5301");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-0000000d5301");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-0000000d5301");
    private static readonly Guid Proposer = Guid.Parse("40000000-0000-0000-0000-0000000d5302");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-0000000d5303");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-0000000d5304");
    private static readonly Guid CutLine = Guid.Parse("50000000-0000-0000-0000-0000000d5301");
    private static readonly DateTimeOffset ProposedAt = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(3));
    private const string Reason = "Kantar dara hatası";

    private static Kilograms Kg(decimal value) => new(value);

    private static PlanOutputLineReference Line => PlanOutputLineReference.ForCutLine(CutLine);

    /// <summary>An open order of an approved coil-to-sheet plan, 1 000 kg issued and one good output on its cut line.</summary>
    private static (ProductionOrder Order, OutputDeclaration Output) OrderWithOutput(decimal mass = 600m,
        OutputClass outputClass = OutputClass.GoodProduct, bool withLine = true)
    {
        var spec = new CutToLengthSpec([new CutToLengthLine(CutLine, 1, 6000m, 3)]);
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(Roll, Kg(1_000m))], cutToLengthSpec: spec);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Issue(Roll, Kg(1_000m));
        PlanOutputLineReference? line = withLine
            ? outputClass == OutputClass.GoodProduct ? Line : PlanOutputLineReference.ClassLine
            : null;
        var output = order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, outputClass, Kg(mass), OrderSteps.Next(),
            line);
        return (order, output);
    }

    private static WeighingCorrection Propose(ProductionOrder order, Guid outputId, decimal correctMass) =>
        WeighingCorrection.Propose(order, order.Version, Guid.NewGuid(), outputId, Kg(correctMass), Reason, Proposer,
            ProposedAt);

    [Fact]
    public void An_approved_correction_reverses_the_wrong_weighing_and_declares_the_correct_one_without_deleting_it()
    {
        var (order, output) = OrderWithOutput();
        var correction = Propose(order, output.OutputId, 580m);
        var versionAfterProposal = order.Version;
        var reversalId = Guid.NewGuid();
        var correctedOutputId = Guid.NewGuid();

        var applied = correction.Approve(order, order.Version, Approver, reversalId, correctedOutputId, DecidedAt);

        // The proposal itself moved nothing on the order.
        Assert.Equal(versionAfterProposal + 2, order.Version);
        Assert.Equal(new OutputReversal(reversalId, output.OutputId, Kg(600m), Reason, DecidedAt.ToUniversalTime()),
            applied.Reversal);
        Assert.Equal((correctedOutputId, Roll, OutputClass.GoodProduct, Kg(580m), (PlanOutputLineReference?)Line),
            (applied.CorrectedOutput.OutputId, applied.CorrectedOutput.ParentRollId, applied.CorrectedOutput.Class,
                applied.CorrectedOutput.Mass, applied.CorrectedOutput.OutputLine));
        // R6: the wrong declaration stays as recorded; its net mass is zero and the line now carries the correct kg.
        Assert.Equal(output, order.Outputs.Single(entry => entry.OutputId == output.OutputId));
        Assert.Equal(Kg(0m), order.NetMassOf(output.OutputId));
        Assert.Equal(Kg(580m), order.NetMassOf(correctedOutputId));
        Assert.Equal(Kg(580m), order.NetOutputOfLine(OutputClass.GoodProduct, Line));
        // The trace: who proposed, who decided, when, the kg on both sides and the facts it caused.
        Assert.Equal((WeighingCorrectionStatus.Approved, Proposer, (Guid?)Approver, (DateTimeOffset?)DecidedAt.ToUniversalTime(),
                (Guid?)reversalId, (Guid?)correctedOutputId),
            (correction.Status, correction.ProposedByUserId, correction.DecidedByUserId, correction.DecidedAtUtc,
                correction.ReversalId, correction.CorrectedOutputId));
        Assert.Equal((order.Id, Company, output.OutputId, Kg(600m), Kg(580m), Reason, ProposedAt.ToUniversalTime()),
            (correction.OrderId, correction.CompanyId, correction.OutputId, correction.RecordedMass,
                correction.CorrectMass, correction.Reason, correction.ProposedAtUtc));
    }

    [Fact]
    public void The_proposer_can_neither_approve_nor_reject_their_own_correction()
    {
        var (order, output) = OrderWithOutput();
        var correction = Propose(order, output.OutputId, 580m);
        var version = order.Version;

        var approve = Assert.Throws<ManufacturingRuleException>(() =>
            correction.Approve(order, order.Version, Proposer, Guid.NewGuid(), Guid.NewGuid(), DecidedAt));
        var reject = Assert.Throws<ManufacturingRuleException>(() => correction.Reject(Proposer, DecidedAt));

        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionSamePerson, approve.Code);
        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionSamePerson, reject.Code);
        Assert.Equal((WeighingCorrectionStatus.Proposed, (Guid?)null), (correction.Status, correction.DecidedByUserId));
        Assert.Equal(version, order.Version);
        Assert.Empty(order.OutputReversals);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(650)]
    public void A_correction_that_is_not_below_the_recorded_mass_is_refused(int correctMass)
    {
        var (order, output) = OrderWithOutput();

        var error = Assert.Throws<ManufacturingRuleException>(() => Propose(order, output.OutputId, correctMass));

        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionNotLower, error.Code);
    }

    [Fact]
    public void A_declared_process_loss_has_no_weighing_to_correct()
    {
        var (order, output) = OrderWithOutput(outputClass: OutputClass.ProcessLoss);

        var error = Assert.Throws<ManufacturingRuleException>(() => Propose(order, output.OutputId, 10m));

        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionNotWeighed, error.Code);
    }

    [Fact]
    public void A_declaration_without_a_plan_output_line_is_refused_like_its_reversal()
    {
        var (order, output) = OrderWithOutput(withLine: false);

        var error = Assert.Throws<ManufacturingRuleException>(() => Propose(order, output.OutputId, 580m));

        Assert.Equal(ManufacturingRuleCodes.OutputLineMissing, error.Code);
    }

    [Fact]
    public void A_closed_order_is_not_reopened_or_changed_by_a_correction()
    {
        var (order, output) = OrderWithOutput(mass: 1_000m);
        var correction = Propose(order, output.OutputId, 990m);
        order.Close(order.Version, Closer, OrderSteps.Next());
        var version = order.Version;

        var proposal = Assert.Throws<ManufacturingRuleException>(() => Propose(order, output.OutputId, 990m));
        var approval = Assert.Throws<ManufacturingRuleException>(() =>
            correction.Approve(order, order.Version, Approver, Guid.NewGuid(), Guid.NewGuid(), DecidedAt));

        Assert.Equal(ManufacturingRuleCodes.OrderClosed, proposal.Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, approval.Code);
        Assert.Equal((OrderStatus.Closed, version), (order.Status, order.Version));
        Assert.Empty(order.OutputReversals);
        Assert.Equal(WeighingCorrectionStatus.Proposed, correction.Status);
    }

    [Fact]
    public void An_output_changed_after_the_proposal_is_refused_at_approval()
    {
        var (order, output) = OrderWithOutput();
        var correction = Propose(order, output.OutputId, 580m);
        order.ReverseOutput(order.Version, Guid.NewGuid(), output.OutputId, Kg(100m), Reason, OrderSteps.Next());
        var version = order.Version;

        var error = Assert.Throws<ManufacturingRuleException>(() =>
            correction.Approve(order, order.Version, Approver, Guid.NewGuid(), Guid.NewGuid(), DecidedAt));

        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionOutputChanged, error.Code);
        Assert.Equal(version, order.Version);
        Assert.Equal(WeighingCorrectionStatus.Proposed, correction.Status);
    }

    [Fact]
    public void A_stale_order_version_or_a_known_output_id_is_refused_before_anything_is_recorded()
    {
        var (order, output) = OrderWithOutput();
        var correction = Propose(order, output.OutputId, 580m);
        var version = order.Version;

        var stale = Assert.Throws<ManufacturingVersionConflictException>(() =>
            correction.Approve(order, version - 1, Approver, Guid.NewGuid(), Guid.NewGuid(), DecidedAt));
        var known = Assert.Throws<ManufacturingRuleException>(() =>
            correction.Approve(order, version, Approver, Guid.NewGuid(), output.OutputId, DecidedAt));

        Assert.NotNull(stale);
        Assert.Equal(ManufacturingRuleCodes.OutputAlreadyDeclared, known.Code);
        Assert.Equal(version, order.Version);
        Assert.Empty(order.OutputReversals);
        Assert.Single(order.Outputs);
        Assert.Equal(WeighingCorrectionStatus.Proposed, correction.Status);
    }

    [Fact]
    public void A_decided_correction_cannot_be_decided_again()
    {
        var (order, output) = OrderWithOutput();
        var approved = Propose(order, output.OutputId, 580m);
        approved.Approve(order, order.Version, Approver, Guid.NewGuid(), Guid.NewGuid(), DecidedAt);
        var (otherOrder, otherOutput) = OrderWithOutput();
        var rejected = Propose(otherOrder, otherOutput.OutputId, 580m);
        rejected.Reject(Approver, DecidedAt);
        var otherVersion = otherOrder.Version;

        var again = Assert.Throws<ManufacturingRuleException>(() => approved.Reject(Approver, DecidedAt));
        var afterReject = Assert.Throws<ManufacturingRuleException>(() =>
            rejected.Approve(otherOrder, otherOrder.Version, Approver, Guid.NewGuid(), Guid.NewGuid(), DecidedAt));

        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionNotProposed, again.Code);
        Assert.Equal(ManufacturingRuleCodes.WeighingCorrectionNotProposed, afterReject.Code);
        Assert.Equal(WeighingCorrectionStatus.Approved, approved.Status);
        Assert.Equal((WeighingCorrectionStatus.Rejected, (Guid?)Approver), (rejected.Status, rejected.DecidedByUserId));
        Assert.Equal(otherVersion, otherOrder.Version);
        Assert.Empty(otherOrder.OutputReversals);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    public void A_correction_without_a_valid_reason_is_refused(string reason)
    {
        var (order, output) = OrderWithOutput();

        Assert.Throws<ArgumentException>(() => WeighingCorrection.Propose(order, order.Version, Guid.NewGuid(),
            output.OutputId, Kg(580m), reason, Proposer, ProposedAt));
    }
}
