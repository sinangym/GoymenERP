using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// F-009 D2 (URT-D28, URT-D30; analysis §4.3/§4.6): an output correction is a new reversal fact linked to its
/// declaration (R6: nothing is deleted or rewritten); the net output is the declaration minus its reversals, never
/// below zero; mass balance and close use net outputs; a closed order refuses every correction; a declaration
/// carries its plan output line and one stored without a line ("satırsız") is refused fail-closed.
/// </summary>
public sealed class OutputReversalTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");
    private static readonly Guid FirstCutLine = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondCutLine = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid ForeignCutLine = Guid.Parse("50000000-0000-0000-0000-000000000009");
    private const string Reason = "Tartım hatası";

    private static Kilograms Kg(decimal value) => new(value);

    /// <summary>An order of an approved coil-to-sheet plan with two cut lines (its plan output lines).</summary>
    private static ProductionOrder CutOrder(ProductionTarget? target = null)
    {
        var spec = new CutToLengthSpec(
        [
            new CutToLengthLine(FirstCutLine, 1, 6000m, 3),
            new CutToLengthLine(SecondCutLine, 2, 2500m, 2),
        ]);
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(Roll, Kg(1_000m))], target, cutToLengthSpec: spec);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    /// <summary>An order of an approved plan without output lines (no slitting and no cut-to-length spec).</summary>
    private static ProductionOrder PlainOrder()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, Kg(1_000m))]);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static OutputDeclaration DeclareOn(ProductionOrder order, OutputClass outputClass, decimal mass,
        PlanOutputLineReference? line) =>
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, outputClass, Kg(mass), OrderSteps.Next(), line);

    private static OutputReversal Reverse(ProductionOrder order, Guid outputId, decimal mass) =>
        order.ReverseOutput(order.Version, Guid.NewGuid(), outputId, Kg(mass), Reason, OrderSteps.Next());

    [Fact]
    public void An_order_carries_its_plan_output_lines_cut_lines_by_id_in_plan_order()
    {
        var order = CutOrder();

        Assert.Equal(
            [PlanOutputLineReference.ForCutLine(FirstCutLine), PlanOutputLineReference.ForCutLine(SecondCutLine)],
            order.PlannedOutputLines);
        Assert.Empty(PlainOrder().PlannedOutputLines);
    }

    [Fact]
    public void A_partial_reversal_leaves_the_declaration_unchanged_and_lowers_its_net_mass()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        var versionBefore = order.Version;

        var reversal = Reverse(order, declaration.OutputId, 150m);

        Assert.Equal(versionBefore + 1, order.Version);
        Assert.Equal(declaration.OutputId, reversal.OutputId);
        Assert.Equal(Reason, reversal.Reason);
        Assert.Equal(600m, Assert.Single(order.Outputs).Mass.Value);
        Assert.Equal(450m, order.NetMassOf(declaration.OutputId).Value);
        Assert.Single(order.OutputReversals);
    }

    [Fact]
    public void Reversals_may_take_back_exactly_the_declared_mass_and_the_net_mass_is_then_zero()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));

        Reverse(order, declaration.OutputId, 400m);
        Reverse(order, declaration.OutputId, 200m);

        Assert.Equal(0m, order.NetMassOf(declaration.OutputId).Value);
        Assert.Equal(2, order.OutputReversals.Count);
    }

    [Fact]
    public void Reversals_adding_up_to_more_than_the_declared_mass_are_refused_and_nothing_changes()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        Reverse(order, declaration.OutputId, 500m);
        var versionBefore = order.Version;

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, declaration.OutputId, 100.001m));

        Assert.Equal(ManufacturingRuleCodes.OutputReversalExceedsDeclared, error.Code);
        Assert.Equal(versionBefore, order.Version);
        Assert.Equal(100m, order.NetMassOf(declaration.OutputId).Value);
        Assert.Single(order.OutputReversals);
    }

    [Fact]
    public void A_closed_order_refuses_a_correction()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 1_000m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        order.Close(order.Version, Closer, OrderSteps.Next());

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, declaration.OutputId, 10m));

        Assert.Equal(ManufacturingRuleCodes.OrderClosed, error.Code);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public void A_declaration_without_a_plan_output_line_is_refused_fail_closed()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var legacy = order.Declare(Roll, OutputClass.GoodProduct, Kg(600m));
        var versionBefore = order.Version;

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, legacy.OutputId, 10m));

        Assert.Equal(ManufacturingRuleCodes.OutputLineMissing, error.Code);
        Assert.Null(legacy.OutputLine);
        Assert.Equal(versionBefore, order.Version);
    }

    [Fact]
    public void A_reversal_of_an_output_never_declared_on_the_order_is_refused()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, Guid.NewGuid(), 10m));

        Assert.Equal(ManufacturingRuleCodes.OutputNotDeclared, error.Code);
    }

    [Fact]
    public void The_same_reversal_id_is_recorded_once()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        var reversalId = Guid.NewGuid();
        order.ReverseOutput(order.Version, reversalId, declaration.OutputId, Kg(10m), Reason, OrderSteps.Next());

        var error = Assert.Throws<ManufacturingRuleException>(() =>
            order.ReverseOutput(order.Version, reversalId, declaration.OutputId, Kg(10m), Reason, OrderSteps.Next()));

        Assert.Equal(ManufacturingRuleCodes.OutputReversalAlreadyRecorded, error.Code);
    }

    [Fact]
    public void A_reversal_against_a_stale_version_is_a_version_conflict()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));

        var error = Assert.Throws<ManufacturingVersionConflictException>(() =>
            order.ReverseOutput(order.Version - 1, Guid.NewGuid(), declaration.OutputId, Kg(10m), Reason,
                OrderSteps.Next()));

        Assert.Equal(order.Version, error.CurrentVersion);
        Assert.Empty(order.OutputReversals);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("satır\nsonu")]
    public void A_reversal_needs_a_reason_without_control_characters(string reason)
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var declaration = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));

        Assert.Throws<ArgumentException>(() =>
            order.ReverseOutput(order.Version, Guid.NewGuid(), declaration.OutputId, Kg(10m), reason, OrderSteps.Next()));
        Assert.Throws<ArgumentException>(() =>
            order.ReverseOutput(order.Version, Guid.NewGuid(), declaration.OutputId, Kg(10m),
                new string('a', OutputReversal.ReasonMaximumLength + 1), OrderSteps.Next()));
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public void The_mass_balance_counts_outputs_net_of_their_reversals()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var good = DeclareOn(order, OutputClass.GoodProduct, 900m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        DeclareOn(order, OutputClass.Scrap, 100m, PlanOutputLineReference.ClassLine);
        Assert.Equal(0m, order.BalanceOf(Roll).Unexplained);

        Reverse(order, good.OutputId, 50m);

        var balance = order.BalanceOf(Roll);
        Assert.Equal(850m, balance.OutputOf(OutputClass.GoodProduct).Value);
        Assert.Equal(50m, balance.Unexplained);
    }

    [Fact]
    public void A_close_is_evaluated_on_net_outputs()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var good = DeclareOn(order, OutputClass.GoodProduct, 1_000m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        Reverse(order, good.OutputId, 100m);

        var refused = Assert.Throws<ManufacturingRuleException>(() =>
            order.Close(order.Version, Closer, OrderSteps.Next()));
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, refused.Code);

        DeclareOn(order, OutputClass.Scrap, 100m, PlanOutputLineReference.ClassLine);
        order.Close(order.Version, Closer, OrderSteps.Next());

        Assert.Equal(OrderStatus.Closed, order.Status);
    }

    [Fact]
    public void The_remaining_good_output_target_counts_good_outputs_net_of_their_reversals()
    {
        var order = CutOrder(new ProductionTarget(TargetBasis.GoodOutputMass, Kg(800m)));
        order.Issue(Roll, Kg(1_000m));
        var good = DeclareOn(order, OutputClass.GoodProduct, 800m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        var reached = order.RemainingTargetOf();

        Reverse(order, good.OutputId, 300m);
        var reopened = order.RemainingTargetOf();

        Assert.NotEqual(reached, reopened);
        Assert.Equal(300m, reopened.Remaining!.Value);
    }

    [Fact]
    public void The_net_output_of_a_line_sums_the_net_masses_of_its_declarations_only()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var first = PlanOutputLineReference.ForCutLine(FirstCutLine);
        var second = PlanOutputLineReference.ForCutLine(SecondCutLine);
        var a = DeclareOn(order, OutputClass.GoodProduct, 300m, first);
        DeclareOn(order, OutputClass.GoodProduct, 200m, first);
        DeclareOn(order, OutputClass.GoodProduct, 250m, second);
        order.Declare(Roll, OutputClass.GoodProduct, Kg(50m));

        Reverse(order, a.OutputId, 100m);

        Assert.Equal(400m, order.NetOutputOfLine(OutputClass.GoodProduct, first).Value);
        Assert.Equal(250m, order.NetOutputOfLine(OutputClass.GoodProduct, second).Value);
        Assert.Equal(0m, order.NetOutputOfLine(OutputClass.Scrap, PlanOutputLineReference.ClassLine).Value);
    }

    [Fact]
    public void Every_output_class_is_reversed_the_same_way()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var scrap = DeclareOn(order, OutputClass.Scrap, 40m, PlanOutputLineReference.ClassLine);
        var loss = DeclareOn(order, OutputClass.ProcessLoss, 10m, PlanOutputLineReference.ClassLine);

        Reverse(order, scrap.OutputId, 40m);
        Reverse(order, loss.OutputId, 5m);

        Assert.Equal(0m, order.NetMassOf(scrap.OutputId).Value);
        Assert.Equal(5m, order.NetOutputOfLine(OutputClass.ProcessLoss, PlanOutputLineReference.ClassLine).Value);
    }

    [Fact]
    public void A_good_product_binds_only_to_one_of_the_plans_output_lines()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));

        var foreign = Assert.Throws<ManufacturingRuleException>(() =>
            DeclareOn(order, OutputClass.GoodProduct, 10m, PlanOutputLineReference.ForCutLine(ForeignCutLine)));
        var slit = Assert.Throws<ManufacturingRuleException>(() =>
            DeclareOn(order, OutputClass.GoodProduct, 10m, PlanOutputLineReference.ForSlitLine(1)));
        var classLine = Assert.Throws<ManufacturingRuleException>(() =>
            DeclareOn(order, OutputClass.GoodProduct, 10m, PlanOutputLineReference.ClassLine));

        Assert.All([foreign, slit, classLine], error => Assert.Equal(ManufacturingRuleCodes.OutputLineNotInPlan, error.Code));
        Assert.Empty(order.Outputs);
    }

    [Fact]
    public void Every_class_other_than_good_product_binds_only_to_its_class_line()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));

        var error = Assert.Throws<ManufacturingRuleException>(() =>
            DeclareOn(order, OutputClass.SecondQuality, 10m, PlanOutputLineReference.ForCutLine(FirstCutLine)));

        Assert.Equal(ManufacturingRuleCodes.OutputLineNotInPlan, error.Code);
        DeclareOn(order, OutputClass.SecondQuality, 10m, PlanOutputLineReference.ClassLine);
        DeclareOn(order, OutputClass.UsableRemnant, 10m, PlanOutputLineReference.ClassLine);
        Assert.Equal(2, order.Outputs.Count);
    }

    [Fact]
    public void A_good_product_of_a_plan_without_output_lines_binds_to_its_class_line()
    {
        var order = PlainOrder();
        order.Issue(Roll, Kg(1_000m));

        var declaration = DeclareOn(order, OutputClass.GoodProduct, 500m, PlanOutputLineReference.ClassLine);
        var error = Assert.Throws<ManufacturingRuleException>(() =>
            DeclareOn(order, OutputClass.GoodProduct, 10m, PlanOutputLineReference.ForSlitLine(1)));

        Assert.Equal(PlanOutputLineReference.ClassLine, declaration.OutputLine);
        Assert.Equal(ManufacturingRuleCodes.OutputLineNotInPlan, error.Code);
        Reverse(order, declaration.OutputId, 500m);
        Assert.Equal(0m, order.NetMassOf(declaration.OutputId).Value);
    }

    [Fact]
    public void A_restored_order_replays_its_reversals_and_lines_and_reaches_the_stored_version()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var good = DeclareOn(order, OutputClass.GoodProduct, 950m, PlanOutputLineReference.ForCutLine(SecondCutLine));
        var legacy = order.Declare(Roll, OutputClass.Scrap, Kg(50m));
        Reverse(order, good.OutputId, 100m);
        DeclareOn(order, OutputClass.ProcessLoss, 100m, PlanOutputLineReference.ClassLine);
        order.Close(order.Version, Closer, OrderSteps.Next());

        var restored = ProductionOrder.Restore(order.Id, order.PlanId, order.CompanyId, order.WorkCenterId,
            order.Process, order.Target, order.PlannedInputs, order.Issues, order.Returns,
            order.Outputs, order.WipMeasurements, order.ClosedByUserId, order.ClosedAtUtc, order.Version,
            plannedOutputLines: order.PlannedOutputLines, outputReversals: order.OutputReversals);

        Assert.Equal(order.Version, restored.Version);
        Assert.Equal(OrderStatus.Closed, restored.Status);
        Assert.Equal(850m, restored.NetMassOf(good.OutputId).Value);
        Assert.Equal(PlanOutputLineReference.ForCutLine(SecondCutLine),
            restored.Outputs.Single(output => output.OutputId == good.OutputId).OutputLine);
        Assert.Null(restored.Outputs.Single(output => output.OutputId == legacy.OutputId).OutputLine);
        Assert.Equal(order.PlannedOutputLines, restored.PlannedOutputLines);
    }

    [Fact]
    public void A_restored_order_whose_stored_version_omits_a_reversal_is_refused()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        var good = DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));
        Reverse(order, good.OutputId, 100m);

        var error = Assert.Throws<ManufacturingRuleException>(() => ProductionOrder.Restore(order.Id, order.PlanId,
            order.CompanyId, order.WorkCenterId, order.Process, order.Target, order.PlannedInputs, order.Issues,
            order.Returns, order.Outputs, order.WipMeasurements, null, null, order.Version - 1,
            plannedOutputLines: order.PlannedOutputLines, outputReversals: order.OutputReversals));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, error.Code);
    }

    [Fact]
    public void A_restored_declaration_on_a_line_outside_the_stored_plan_lines_is_refused()
    {
        var order = CutOrder();
        order.Issue(Roll, Kg(1_000m));
        DeclareOn(order, OutputClass.GoodProduct, 600m, PlanOutputLineReference.ForCutLine(FirstCutLine));

        var error = Assert.Throws<ManufacturingRuleException>(() => ProductionOrder.Restore(order.Id, order.PlanId,
            order.CompanyId, order.WorkCenterId, order.Process, order.Target, order.PlannedInputs, order.Issues,
            order.Returns, order.Outputs, order.WipMeasurements, null, null, order.Version,
            plannedOutputLines: [PlanOutputLineReference.ForCutLine(SecondCutLine)]));

        Assert.Equal(ManufacturingRuleCodes.OutputLineNotInPlan, error.Code);
    }

    [Fact]
    public void Stored_plan_output_lines_are_each_a_plan_line_once()
    {
        var order = CutOrder();
        var duplicate = PlanOutputLineReference.ForCutLine(FirstCutLine);

        var twice = Assert.Throws<ManufacturingRuleException>(() => RestoreWithLines(order, [duplicate, duplicate]));
        var classLine = Assert.Throws<ManufacturingRuleException>(() =>
            RestoreWithLines(order, [PlanOutputLineReference.ClassLine]));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, twice.Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, classLine.Code);
    }

    [Fact]
    public void A_stored_line_reference_is_rebuilt_only_from_a_well_formed_combination()
    {
        Assert.Equal(PlanOutputLineReference.ForSlitLine(2),
            PlanOutputLineReference.FromStored(PlanOutputLineKind.SlitLine, 2, null));
        Assert.Equal(PlanOutputLineReference.ForCutLine(FirstCutLine),
            PlanOutputLineReference.FromStored(PlanOutputLineKind.CutLine, null, FirstCutLine));
        Assert.Same(PlanOutputLineReference.ClassLine,
            PlanOutputLineReference.FromStored(PlanOutputLineKind.ClassLine, null, null));

        Assert.Throws<ArgumentException>(() => PlanOutputLineReference.FromStored(PlanOutputLineKind.SlitLine, null, null));
        Assert.Throws<ArgumentException>(() =>
            PlanOutputLineReference.FromStored(PlanOutputLineKind.CutLine, 1, FirstCutLine));
        Assert.Throws<ArgumentException>(() => PlanOutputLineReference.FromStored(PlanOutputLineKind.ClassLine, 1, null));
        Assert.ThrowsAny<ArgumentException>(() => PlanOutputLineReference.FromStored(PlanOutputLineKind.SlitLine, 0, null));
        Assert.Throws<ArgumentException>(() => PlanOutputLineReference.FromStored((PlanOutputLineKind)9, null, null));
    }

    private static ProductionOrder RestoreWithLines(ProductionOrder order, PlanOutputLineReference[] lines) =>
        ProductionOrder.Restore(order.Id, order.PlanId, order.CompanyId, order.WorkCenterId, order.Process,
            order.Target, order.PlannedInputs, [], [], [], [], null, null, order.Version,
            plannedOutputLines: lines);
}
