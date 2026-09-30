using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The MFG-1 decisions: the remaining target is measured only in the unit the plan DECLARES, otherwise it is
/// unavailable; the runtime close remains fail-closed for any unexplained difference. OPEN-005 / Ö7 preparation is
/// read-only and uses only an explicitly supplied policy snapshot and BAL-004 inputs; it records neither fire nor an
/// approval and does not soften <see cref="ProductionOrder.Close"/>.
/// </summary>
public sealed class OrderTargetAndCloseTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid OtherRoll = Guid.Parse("30000000-0000-0000-0000-00000000a002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");
    private static readonly Guid Policy = Guid.Parse("50000000-0000-0000-0000-00000000c001");

    private static Kilograms Kg(decimal value) => new(value);

    private static ClosureTolerancePolicySnapshotV1 Tolerance(decimal percent,
        Guid? companyId = null, ManufacturingProcess process = ManufacturingProcess.Slitting) =>
        new(companyId ?? Company, Policy, 7, process, percent);

    private static RollScrapPercentInputV1 Fire(Guid roll, decimal? fireKg, decimal? rawMaterialKg) =>
        new(roll, new ScrapPercentInputV1(fireKg, rawMaterialKg));

    private static ProductionOrder Order(ProductionTarget? target, params PlannedInput[] inputs)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            inputs.Length == 0 ? new[] { new PlannedInput(MasterRoll, Kg(10_000m)) } : inputs, target);
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void Without_a_declared_target_the_remaining_target_is_unavailable_not_guessed()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));

        var remaining = order.RemainingTargetOf();

        Assert.False(remaining.IsAvailable);
        Assert.Null(remaining.Basis);
        Assert.Null(remaining.Target);
        Assert.Null(remaining.Achieved);
        Assert.Null(remaining.Remaining);
    }

    [Fact]
    public void An_input_mass_target_counts_input_issued_net_of_returns()
    {
        var order = Order(new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));
        order.Issue(MasterRoll, Kg(6_000m));
        order.Return(MasterRoll, Kg(1_000m));
        // Output does not count toward an input target.
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_000m));

        var remaining = order.RemainingTargetOf();

        Assert.True(remaining.IsAvailable);
        Assert.Equal(TargetBasis.InputMass, remaining.Basis);
        Assert.Equal(5_000m, remaining.Achieved!.Value.Value);
        Assert.Equal(5_000m, remaining.Remaining);
    }

    [Fact]
    public void A_good_output_target_counts_good_product_only()
    {
        var order = Order(new ProductionTarget(TargetBasis.GoodOutputMass, Kg(9_700m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.SecondQuality, Kg(50m));

        Assert.Equal(4_850m, order.RemainingTargetOf().Remaining);

        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        Assert.Equal(0m, order.RemainingTargetOf().Remaining);
    }

    [Fact]
    public void An_exceeded_target_is_reported_with_its_sign()
    {
        var order = Order(new ProductionTarget(TargetBasis.GoodOutputMass, Kg(9_000m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));

        Assert.Equal(-700m, order.RemainingTargetOf().Remaining);
    }

    [Fact]
    public void An_order_with_an_unexplained_difference_does_not_close_and_keeps_it_visible()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));

        var error = Assert.Throws<ManufacturingRuleException>(() => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch));

        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, error.Code);
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Null(order.ClosedByUserId);
        // Still unexplained, still not written off as loss.
        Assert.Equal(100m, order.BalanceOf(MasterRoll).Unexplained);
        Assert.Equal(0m, order.BalanceOf(MasterRoll).OutputOf(OutputClass.ProcessLoss).Value);
    }

    [Fact]
    public void One_unbalanced_roll_keeps_the_whole_order_open()
    {
        var order = Order(null, new PlannedInput(MasterRoll, Kg(10_000m)), new PlannedInput(OtherRoll, Kg(5_000m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Issue(OtherRoll, Kg(5_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(10_000m));
        order.Declare(OtherRoll, OutputClass.GoodProduct, Kg(4_999m));

        var error = Assert.Throws<ManufacturingRuleException>(() => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch));
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, error.Code);
        Assert.Contains(OtherRoll.ToString(), error.Message);
    }

    [Fact]
    public void A_fully_accounted_order_closes_and_then_takes_no_more_realization()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(100m));

        order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch);

        Assert.Equal(OrderStatus.Closed, order.Status);
        Assert.Equal(Closer, order.ClosedByUserId);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Issue(MasterRoll, Kg(1m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(MasterRoll, OutputClass.Scrap, Kg(1m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Return(MasterRoll, Kg(1m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.MeasureWip(MasterRoll, Kg(1m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code);
    }

    [Fact]
    public void An_unexplained_gap_requires_an_owned_fire_declaration_and_is_never_an_automatic_numerator()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(990m));
        var version = order.Version;
        var facts = (order.Issues.Count, order.Outputs.Count, order.Returns.Count, order.WipMeasurements.Count);
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, Assert.Throws<ManufacturingRuleException>(
            () => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code);

        var inside = order.AssessClosure(Tolerance(1m), [Fire(MasterRoll, 10m, 1_000m)]);
        var outside = order.AssessClosure(Tolerance(0.999m), [Fire(MasterRoll, 10m, 1_000m)]);

        Assert.Equal(ClosureDispositionV1.FireDeclarationRequired, inside.Disposition);
        Assert.Equal((ClosureDispositionV1.FireDeclarationRequired, 10m, (decimal?)null),
            (Assert.Single(inside.Rolls).Disposition, inside.Rolls[0].DifferenceKg, inside.Rolls[0].FirePercent));
        Assert.Equal(ClosureDispositionV1.FireDeclarationRequired, outside.Disposition);
        Assert.Contains("scrapOrProcessLossDeclaration", Assert.Single(outside.Rolls).MissingParameters);
        Assert.Equal((version, facts, OrderStatus.Open),
            (order.Version, (order.Issues.Count, order.Outputs.Count, order.Returns.Count, order.WipMeasurements.Count),
                order.Status));
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, Assert.Throws<ManufacturingRuleException>(
            () => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code);
    }

    [Fact]
    public void Declared_scrap_and_non_stock_process_loss_are_the_numerator_and_second_quality_is_not()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(800m));
        order.Declare(MasterRoll, OutputClass.SecondQuality, Kg(50m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(100m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(50m));

        var inclusive = order.AssessClosure(Tolerance(15m), [Fire(MasterRoll, 150m, 1_000m)]);
        var outside = order.AssessClosure(Tolerance(14.999m), [Fire(MasterRoll, 150m, 1_000m)]);

        Assert.Equal((ClosureDispositionV1.Balanced, 0m, 15m),
            (inclusive.Disposition, Assert.Single(inclusive.Rolls).DifferenceKg, inclusive.Rolls[0].FirePercent));
        Assert.Equal(ClosureDispositionV1.ApprovalRequired, outside.Disposition);
    }

    [Fact]
    public void Each_declared_fire_component_is_counted_once()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(980m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(10m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(10m));

        var exact = order.AssessClosure(Tolerance(2m), [Fire(MasterRoll, 20m, 1_000m)]);
        var doubled = order.AssessClosure(Tolerance(4m), [Fire(MasterRoll, 40m, 1_000m)]);

        Assert.Equal((ClosureDispositionV1.Balanced, 0m, 2m),
            (exact.Disposition, Assert.Single(exact.Rolls).DifferenceKg, exact.Rolls[0].FirePercent));
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.FireMassMismatch, "fireKg"),
            (doubled.Disposition, Assert.Single(doubled.Rolls).Code, doubled.Rolls[0].RefusedParameter));
    }

    [Fact]
    public void BAL004_denominator_is_the_exact_issued_minus_returned_mass()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Return(MasterRoll, Kg(200m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(790m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(5m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(5m));

        var exact = order.AssessClosure(Tolerance(1.25m), [Fire(MasterRoll, 10m, 800m)]);
        var issuedInsteadOfNet = order.AssessClosure(Tolerance(1.25m), [Fire(MasterRoll, 10m, 1_000m)]);

        Assert.Equal((ClosureDispositionV1.Balanced, 0m, 1.25m),
            (exact.Disposition, Assert.Single(exact.Rolls).DifferenceKg, exact.Rolls[0].FirePercent));
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.NetIssuedMassMismatch, "rawMaterialKg"),
            (issuedInsteadOfNet.Disposition, Assert.Single(issuedInsteadOfNet.Rolls).Code,
                issuedInsteadOfNet.Rolls[0].RefusedParameter));
    }

    [Fact]
    public void Exception_approval_is_bound_to_actor_reason_order_version_and_policy_version()
    {
        var policy = Tolerance(1m);
        var approval = ClosureExceptionApprovalV1.Create(Closer, "  Teyitli proses sapması  ", 7,
            policy.PolicyId, policy.PolicyVersion, DateTimeOffset.Parse("2026-09-30T18:30:00+03:00"));

        Assert.Equal("Teyitli proses sapması", approval.Reason);
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T15:30:00Z"), approval.ApprovedAtUtc);
        Assert.True(approval.Matches(Closer, 7, policy));
        Assert.False(approval.Matches(Guid.NewGuid(), 7, policy));
        Assert.False(approval.Matches(Closer, 8, policy));
        Assert.False(approval.Matches(Closer, 7, policy with { PolicyVersion = 8 }));
    }

    [Theory]
    [InlineData("empty-actor")]
    [InlineData("empty-reason")]
    [InlineData("control-reason")]
    [InlineData("order-version")]
    [InlineData("policy-id")]
    [InlineData("policy-version")]
    [InlineData("time")]
    public void Invalid_exception_approval_evidence_is_refused(string fault)
    {
        var exception = Assert.Throws<ArgumentException>(() => ClosureExceptionApprovalV1.Create(
            fault == "empty-actor" ? Guid.Empty : Closer,
            fault == "empty-reason" ? " " : fault == "control-reason" ? "bad\nreason" : "Geçerli gerekçe",
            fault == "order-version" ? 0 : 7,
            fault == "policy-id" ? Guid.Empty : Policy,
            fault == "policy-version" ? 0 : 3,
            fault == "time" ? default : DateTimeOffset.UnixEpoch));

        Assert.NotEmpty(exception.Message);
    }

    [Fact]
    public void Rolls_never_offset_and_a_negative_difference_requires_review_not_approval()
    {
        var order = Order(null,
            new PlannedInput(MasterRoll, Kg(1_000m)), new PlannedInput(OtherRoll, Kg(1_000m)));
        order.Issue(MasterRoll, Kg(1_000m));
        order.Issue(OtherRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(1_010m));
        order.Declare(OtherRoll, OutputClass.GoodProduct, Kg(990m));

        var assessment = order.AssessClosure(Tolerance(1m), [Fire(OtherRoll, 10m, 1_000m)]);

        Assert.Equal(ClosureDispositionV1.ReviewRequired, assessment.Disposition);
        Assert.Equal((ClosureDispositionV1.ReviewRequired, -10m,
                ClosureAssessmentCodesV1.NegativeDifferenceReview),
            (assessment.Rolls[0].Disposition, assessment.Rolls[0].DifferenceKg, assessment.Rolls[0].Code));
        Assert.Equal((ClosureDispositionV1.FireDeclarationRequired, 10m, (decimal?)null),
            (assessment.Rolls[1].Disposition, assessment.Rolls[1].DifferenceKg, assessment.Rolls[1].FirePercent));
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, Assert.Throws<ManufacturingRuleException>(
            () => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code);
    }

    [Theory]
    [InlineData(null, 1_000, ClosureDispositionV1.Unavailable, null, "fireKg")]
    [InlineData(10, null, ClosureDispositionV1.Unavailable, null, "rawMaterialKg")]
    [InlineData(10, 0, ClosureDispositionV1.Refused, EngineCodes.ValueNotPositive, null)]
    [InlineData(-10, 1_000, ClosureDispositionV1.Refused, EngineCodes.ValueNegative, null)]
    public void Missing_or_invalid_BAL004_inputs_fail_closed(
        int? fireKg, int? rawMaterialKg, ClosureDispositionV1 disposition, string? code, string? missing)
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(990m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(10m));

        var assessment = order.AssessClosure(Tolerance(1m), [Fire(MasterRoll, fireKg, rawMaterialKg)]);

        var roll = Assert.Single(assessment.Rolls);
        Assert.Equal(disposition, assessment.Disposition);
        Assert.Equal(code, roll.Code);
        if (missing is not null)
        {
            Assert.Contains(missing, roll.MissingParameters);
            Assert.Contains(missing, assessment.MissingParameters);
        }
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void A_missing_or_different_fire_classification_never_becomes_an_automatic_close()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(990m));

        var missing = order.AssessClosure(Tolerance(1m), []);
        var different = order.AssessClosure(Tolerance(1m), [Fire(MasterRoll, 5m, 1_000m)]);

        Assert.Equal(ClosureDispositionV1.FireDeclarationRequired, missing.Disposition);
        Assert.Equal(ClosureDispositionV1.FireDeclarationRequired, different.Disposition);
        Assert.Contains("scrapOrProcessLossDeclaration", Assert.Single(missing.Rolls).MissingParameters);
        Assert.Contains("scrapOrProcessLossDeclaration", Assert.Single(different.Rolls).MissingParameters);
        Assert.Equal(0m, order.BalanceOf(MasterRoll).OutputOf(OutputClass.Scrap).Value);
    }

    [Fact]
    public void Foreign_policy_process_and_roll_inputs_fail_closed_with_no_state_change()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(990m));
        var version = order.Version;

        var foreignCompany = order.AssessClosure(Tolerance(1m, Guid.NewGuid()), [Fire(MasterRoll, 10m, 1_000m)]);
        var otherProcess = order.AssessClosure(Tolerance(1m, process: ManufacturingProcess.ColdRolling),
            [Fire(MasterRoll, 10m, 1_000m)]);
        var otherRoll = order.AssessClosure(Tolerance(1m), [Fire(OtherRoll, 10m, 1_000m)]);

        Assert.Equal((ClosureDispositionV1.Unavailable, ClosureAssessmentCodesV1.PolicyScopeMismatch, "companyId"),
            (foreignCompany.Disposition, foreignCompany.Code, foreignCompany.RefusedParameter));
        Assert.Equal((ClosureDispositionV1.Unavailable, ClosureAssessmentCodesV1.PolicyScopeMismatch, "process"),
            (otherProcess.Disposition, otherProcess.Code, otherProcess.RefusedParameter));
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.RollInputInvalid, "parentRollId"),
            (otherRoll.Disposition, otherRoll.Code, otherRoll.RefusedParameter));
        Assert.Equal((version, OrderStatus.Open), (order.Version, order.Status));
    }

    [Fact]
    public void A_balanced_order_is_only_BAL003_balanced_and_assessment_changes_nothing()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(1_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(1_000m));
        var version = order.Version;

        var missingDenominator = order.AssessClosure(Tolerance(0m), []);
        var assessment = order.AssessClosure(Tolerance(0m), [Fire(MasterRoll, 0m, 1_000m)]);

        Assert.Equal(ClosureDispositionV1.Unavailable, missingDenominator.Disposition);
        Assert.Contains("rawMaterialKg", Assert.Single(missingDenominator.Rolls).MissingParameters);
        Assert.Equal(ClosureDispositionV1.Balanced, assessment.Disposition);
        Assert.Equal(ClosureDispositionV1.Balanced, Assert.Single(assessment.Rolls).Disposition);
        Assert.Equal((version, 0m, OrderStatus.Open),
            (order.Version, order.BalanceOf(MasterRoll).Unexplained, order.Status));
    }

    [Fact]
    public void The_order_keeps_the_target_it_was_created_with()
    {
        var target = new ProductionTarget(TargetBasis.InputMass, Kg(10_000m));
        var order = Order(target);
        Assert.Equal(target, order.Target);
    }
}
