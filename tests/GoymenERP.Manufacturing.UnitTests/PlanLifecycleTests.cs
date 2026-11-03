using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim U2-a (URT-D1, URT-D2, URT-D9; plan v1 §5.2 API-10/11/15): the plan's rejection (stays a Draft, recorded),
/// cancellation (Draft or approved without an order; kept), one order per plan, the plan-entered line speed (wins over the
/// profile, captured with source <c>Plan</c>) and net usable width (bounded by the parent coil), the slitting gate's label
/// state, and the stored replay of all of it.
/// </summary>
public sealed class PlanLifecycleTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000e001");
    private static readonly Guid OtherWorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000e002");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000e001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000e001");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-00000000e002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000e001");
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly DateTimeOffset At = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    private static PlanTechnicalParameterSnapshotV1 Technical(decimal thicknessMm, decimal widthMm) => new(
        Guid.NewGuid(), "1", Guid.NewGuid(), "SAC", "r1", Guid.NewGuid(), "1", "T", "r1", Company, Guid.NewGuid(), "kg",
        Guid.NewGuid(), "1", Today, thicknessMm, widthMm);

    private static readonly PlanTechnicalParameterSnapshotV1 Coil = Technical(1.000m, 1_000.000m);

    private static PlannedInput Input(decimal kg = 7_850m) => new(Roll, Kg(kg), 1, Coil, null);

    private static PlanParameterSnapshotV1 Snapshot(ManufacturingProcess process,
        params (string Key, string Unit, decimal Value)[] values) =>
        new(Profile, "0", PlanParameterSnapshotV1.CatalogSource, process, null, new DateOnly(2026, 11, 1), null, Today,
            values.Select(value => new PlanParameterValueV1(value.Key, value.Unit, value.Value, null)));

    private static ProductionPlan Draft(ManufacturingProcess process = ManufacturingProcess.ColdRolling,
        decimal? lineSpeed = null) =>
        new(Guid.NewGuid(), Company, WorkCenter, process, [Input()], lineSpeedMetresPerMinute: lineSpeed);

    private static ProductionPlan Approved()
    {
        var plan = Draft();
        plan.Approve(plan.Version, Approver, At);
        return plan;
    }

    [Fact]
    public void A_rejection_keeps_the_plan_a_draft_records_the_reason_and_moves_the_version()
    {
        var plan = Draft();

        plan.Reject(1, Approver, At, "  Rulo miktarı yanlış  ");

        Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.Equal(2, plan.Version);
        var rejection = Assert.Single(plan.Rejections);
        Assert.Equal(new PlanRejection(1, "Rulo miktarı yanlış", Approver, At, 2), rejection);
        // The planner revises at the version after the rejection; the approver approves later.
        plan.Revise(2, OtherWorkCenter, ManufacturingProcess.ColdRolling, [Input(7_000m)], null);
        plan.Approve(3, Approver, At);
        Assert.Equal(PlanStatus.Approved, plan.Status);
    }

    [Fact]
    public void A_rejection_needs_a_reason_the_current_version_and_a_draft()
    {
        var plan = Draft();
        Assert.Throws<ArgumentException>(() => plan.Reject(1, Approver, At, "  "));
        Assert.Throws<ArgumentException>(() => plan.Reject(1, Approver, At, new string('x', 257)));
        Assert.Throws<ManufacturingVersionConflictException>(() => plan.Reject(2, Approver, At, "neden"));
        Assert.Empty(plan.Rejections);
        Assert.Equal(1, plan.Version);

        var approved = Approved();
        Assert.Equal(ManufacturingRuleCodes.PlanNotDraft,
            Assert.Throws<ManufacturingRuleException>(() => approved.Reject(2, Approver, At, "neden")).Code);
    }

    [Fact]
    public void A_draft_and_an_approved_plan_without_an_order_are_cancelled_and_kept()
    {
        var draft = Draft();
        draft.Cancel(1, Planner, At, "Sipariş iptal edildi");
        Assert.Equal(PlanStatus.Cancelled, draft.Status);
        Assert.Equal(2, draft.Version);
        Assert.Equal(new PlanCancellation("Sipariş iptal edildi", Planner, At), draft.Cancellation);
        Assert.Null(draft.ApprovedAtUtc);

        var approved = Approved();
        approved.Cancel(2, Planner, At, "Hat arızası");
        Assert.Equal(PlanStatus.Cancelled, approved.Status);
        Assert.Equal(3, approved.Version);
        // The approval trace stays on the cancelled plan.
        Assert.Equal(Approver, approved.ApprovedByUserId);
    }

    [Fact]
    public void A_plan_that_opened_its_order_is_not_cancelled_and_a_cancelled_plan_takes_no_decision()
    {
        var approved = Approved();
        ProductionOrder.FromApprovedPlan(Guid.NewGuid(), approved);
        Assert.Equal(ManufacturingRuleCodes.PlanHasOrder,
            Assert.Throws<ManufacturingRuleException>(() => approved.Cancel(2, Planner, At, "neden")).Code);
        Assert.Equal(PlanStatus.Approved, approved.Status);

        var cancelled = Draft();
        cancelled.Cancel(1, Planner, At, "neden");
        Assert.Equal(ManufacturingRuleCodes.PlanCancelled,
            Assert.Throws<ManufacturingRuleException>(() => cancelled.Cancel(2, Planner, At, "yine")).Code);
        Assert.Equal(ManufacturingRuleCodes.PlanCancelled,
            Assert.Throws<ManufacturingRuleException>(() => cancelled.Reject(2, Approver, At, "yine")).Code);
        Assert.Equal(ManufacturingRuleCodes.PlanNotDraft,
            Assert.Throws<ManufacturingRuleException>(() => cancelled.Approve(2, Approver, At)).Code);
        Assert.Equal(ManufacturingRuleCodes.PlanNotDraft, Assert.Throws<ManufacturingRuleException>(
            () => cancelled.Revise(2, WorkCenter, ManufacturingProcess.ColdRolling, [Input(1m)], null)).Code);
        Assert.Equal(ManufacturingRuleCodes.PlanNotApproved, Assert.Throws<ManufacturingRuleException>(
            () => ProductionOrder.FromApprovedPlan(Guid.NewGuid(), cancelled)).Code);
        Assert.Equal(2, cancelled.Version);
    }

    [Fact]
    public void Restore_replays_interleaved_revisions_and_rejections_then_the_approval_order_and_cancellation()
    {
        var id = Guid.NewGuid();
        var order = Guid.NewGuid();
        var revisions = new[]
        {
            new PlanRevisionContent(OtherWorkCenter, [Input(7_000m)], null, PlanVersion: 3),
            new PlanRevisionContent(WorkCenter, [Input(6_000m)], null, PlanVersion: 5),
        };
        var rejections = new[]
        {
            new PlanRejection(1, "bir", Approver, At, 2),
            new PlanRejection(2, "iki", Approver, At, 4),
        };

        var plan = ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null,
            Approver, At, 6, revisions: revisions, rejections: rejections, orderId: order);

        Assert.Equal(PlanStatus.Approved, plan.Status);
        Assert.Equal(order, plan.OrderId);
        Assert.Equal(Kg(6_000m), Assert.Single(plan.Inputs).PlannedMass);
        Assert.Equal(rejections, plan.Rejections);

        var cancelled = ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null,
            Approver, At, 7, revisions: revisions, rejections: rejections,
            cancellation: new PlanCancellation("iptal", Planner, At));
        Assert.Equal(PlanStatus.Cancelled, cancelled.Status);

        // A stored version that the steps do not add up to, a rejection out of order, or a cancelled plan with an order
        // is refused, never repaired.
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null,
                Approver, At, 6, revisions: revisions, rejections: [new PlanRejection(1, "bir", Approver, At, 9)])).Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null,
                Approver, At, 7, revisions: revisions, rejections: rejections, orderId: order,
                cancellation: new PlanCancellation("iptal", Planner, At))).Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null,
                null, null, 1, orderId: order)).Code);
    }

    [Fact]
    public void The_plan_entered_line_speed_belongs_to_slitting_and_tube_and_is_part_of_a_revision()
    {
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.Paint, 20m));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.ColdRolling, 20m));
        Assert.ThrowsAny<ArgumentException>(() => Draft(ManufacturingProcess.TubeProfile, 0m));
        Assert.ThrowsAny<ArgumentException>(() => Draft(ManufacturingProcess.TubeProfile, 1.0000001m));

        var tube = Draft(ManufacturingProcess.TubeProfile, 30m);
        Assert.Equal(30m, tube.LineSpeedMetresPerMinute);
        // Only the line speed changes: a real change.
        tube.Revise(1, WorkCenter, ManufacturingProcess.TubeProfile, [Input()], null, lineSpeedMetresPerMinute: 35m);
        Assert.Equal(35m, tube.LineSpeedMetresPerMinute);
        Assert.Equal(ManufacturingRuleCodes.PlanRevisionNoChange, Assert.Throws<ManufacturingRuleException>(
            () => tube.Revise(2, WorkCenter, ManufacturingProcess.TubeProfile, [Input()], null,
                lineSpeedMetresPerMinute: 35m)).Code);
    }

    [Fact]
    public void The_plan_entered_line_speed_wins_over_the_profile_and_is_captured_with_source_plan()
    {
        var plan = Draft(ManufacturingProcess.TubeProfile, 30m);
        var profile = Snapshot(ManufacturingProcess.TubeProfile, ("lineSpeedMetresPerMinute", "m/min", 12m));

        var calculation = ProductionPlanCalculatorV1.Calculate(plan,
            PlanCalculationParameters.FromCapturedSnapshots(plan.Inputs, profile)
                .WithPlanValue("packageCount", 2m).WithPlanValue("piecesPerPackage", 5m)
                .WithPlanValue("pieceLengthMm", 6_000m));
        var run = calculation.PlanAnswers.Single(answer => answer.Formula == "TUB-001…003");
        Assert.Equal(EngineOutcome.Computed, run.Outcome);
        // 10 pieces × 6 m = 60 m at the plan's 30 m/min (not the profile's 12) = 2 minutes.
        Assert.Equal(2m, run.Values.Single(value => value.Name == "runMinutes").Value);

        plan.Approve(1, Approver, At, parameters: null, profile);
        var captured = Assert.Single(plan.ParameterSnapshot!.Values);
        Assert.Equal(new PlanParameterValueV1("lineSpeedMetresPerMinute", "m/min", 30m, null,
            PlanParameterValueV1.PlanSource), captured);

        // The stored snapshot must carry the plan's own value; another number is a stored record the plan refuses.
        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.TubeProfile,
            plan.Inputs, null, Approver, At, 2, parameterSnapshot: plan.ParameterSnapshot, lineSpeedMetresPerMinute: 30m);
        Assert.Equal(plan.ParameterSnapshot, restored.ParameterSnapshot);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.TubeProfile, plan.Inputs,
                null, Approver, At, 2, parameterSnapshot: plan.ParameterSnapshot, lineSpeedMetresPerMinute: 31m)).Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.TubeProfile, plan.Inputs,
                null, Approver, At, 2, parameterSnapshot: profile, lineSpeedMetresPerMinute: 30m)).Code);
    }

    private static ProductionPlan SlittingPlan(decimal? netWidth, decimal? lineSpeed) =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, [Input()],
            spec: new SlittingSpec([new SlitLine(490m, 2)], netUsableWidthMm: netWidth),
            lineSpeedMetresPerMinute: lineSpeed);

    [Fact]
    public void The_net_usable_width_cannot_exceed_a_parent_coil_and_keeps_O2_precision()
    {
        Assert.Equal(ManufacturingRuleCodes.PlanNetWidthExceedsParent,
            Assert.Throws<ManufacturingRuleException>(() => SlittingPlan(1_000.001m, null)).Code);
        Assert.Equal(ManufacturingRuleCodes.MillimetrePrecisionExceeded,
            Assert.Throws<ManufacturingRuleException>(() => SlittingPlan(980.0001m, null)).Code);
        Assert.Equal(1_000m, SlittingPlan(1_000m, null).Spec!.NetUsableWidthMm);
    }

    [Fact]
    public void The_slitting_gate_takes_the_plans_net_width_and_line_speed_and_Inventorys_label_state()
    {
        var density = Snapshot(ManufacturingProcess.Slitting, ("densityKgPerCubicMetre", "kg/m3", 7_850m));

        // Without the plan's entries the gate names them (and the label state no read supplied).
        var bare = SlittingPlan(null, null);
        var missing = Assert.Throws<ManufacturingRuleException>(() => bare.Approve(1, Approver, At,
            SlittingPlanParameters.FromCaptured(bare.Inputs, density), density));
        Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable, missing.Code);
        Assert.Equal(["labelState", "lineSpeedMetresPerMinute", "netUsableWidthMm"], missing.Parameters);

        // A blocked or unusable roll is refused.
        var blocked = SlittingPlan(980m, 50m);
        Assert.Equal(ManufacturingRuleCodes.PlanLabelNotUsable, Assert.Throws<ManufacturingRuleException>(
            () => blocked.Approve(1, Approver, At,
                SlittingPlanParameters.FromCaptured(blocked.Inputs, density,
                    new Dictionary<Guid, bool> { [Roll] = false }), density)).Code);
        Assert.Equal(PlanStatus.Draft, blocked.Status);

        // Everything known and usable: approved, the line speed captured from the plan.
        var plan = SlittingPlan(980m, 50m);
        plan.Approve(1, Approver, At,
            SlittingPlanParameters.FromCaptured(plan.Inputs, density, new Dictionary<Guid, bool> { [Roll] = true }),
            density);
        Assert.Equal(PlanStatus.Approved, plan.Status);
        Assert.Contains(plan.ParameterSnapshot!.Values, value =>
            value is { Key: "lineSpeedMetresPerMinute", Number: 50m, Source: PlanParameterValueV1.PlanSource });

        // The calculator reads the same entries: SLT-001…007 against the plan's net width.
        var slitting = ProductionPlanCalculatorV1.Calculate(plan,
                PlanCalculationParameters.FromCapturedSnapshots(plan.Inputs, plan.ParameterSnapshot))
            .Rolls.Single().Answers.Single(answer => answer.Formula == "SLT-001…007");
        Assert.Equal(EngineOutcome.Computed, slitting.Outcome);
        Assert.Equal(0m, slitting.Values.Single(value => value.Name == "remainingWidthMm").Value);
    }
}
