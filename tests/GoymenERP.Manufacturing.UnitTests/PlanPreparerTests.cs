using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim U2-d (URT-D2 "hazırlayan ≠ onaylayan", plan v1 §5.2 API-10): the user who prepared the plan's current version
/// (saved it, or recorded its last revision) can neither approve nor reject it; another user decides. A plan without a
/// recorded preparer (stored before U2-d) is not refused, and the stored replay never re-checks old decisions.
/// </summary>
public sealed class PlanPreparerTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000d001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d001");
    private static readonly Guid Reviser = Guid.Parse("40000000-0000-0000-0000-00000000d002");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-00000000d003");
    private static readonly DateTimeOffset At = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static PlannedInput Input(decimal kg = 1_000m) => new(Roll, new Kilograms(kg), 1, null, null);

    private static ProductionPlan Draft(Guid? preparer = null)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()]);
        if (preparer is { } user)
        {
            plan.RecordPreparer(user.ToString("D"));
        }

        return plan;
    }

    [Fact]
    public void The_preparer_cannot_approve_the_plan_and_nothing_changes()
    {
        var plan = Draft(Planner);

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(1, Planner, At));

        Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, refusal.Code);
        Assert.Equal((PlanStatus.Draft, 1L), (plan.Status, plan.Version));
        Assert.Null(plan.ApprovedByUserId);
    }

    [Fact]
    public void The_preparer_cannot_reject_the_plan_and_nothing_changes()
    {
        var plan = Draft(Planner);

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Reject(1, Planner, At, "kendi planım"));

        Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, refusal.Code);
        Assert.Equal((PlanStatus.Draft, 1L), (plan.Status, plan.Version));
        Assert.Empty(plan.Rejections);
    }

    [Fact]
    public void Another_user_approves_and_rejects_the_plan()
    {
        var rejected = Draft(Planner);
        rejected.Reject(1, Approver, At, "miktar yanlış");
        Assert.Equal(2L, rejected.Version);

        var approved = Draft(Planner);
        approved.Approve(1, Approver, At);
        Assert.Equal((PlanStatus.Approved, Approver), (approved.Status, approved.ApprovedByUserId));
    }

    [Fact]
    public void After_another_user_revised_the_plan_its_creator_may_decide_but_the_reviser_may_not()
    {
        var plan = Draft(Planner);
        plan.Revise(1, WorkCenter, ManufacturingProcess.ColdRolling, [Input(900m)], null);
        plan.RecordPreparer(Reviser.ToString("D"));

        Assert.True(plan.IsPreparedBy(Reviser));
        Assert.False(plan.IsPreparedBy(Planner));
        Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval,
            Assert.Throws<ManufacturingRuleException>(() => plan.Approve(2, Reviser, At)).Code);

        plan.Approve(2, Planner, At);
        Assert.Equal((PlanStatus.Approved, Planner), (plan.Status, plan.ApprovedByUserId));
    }

    [Fact]
    public void A_plan_without_a_recorded_preparer_is_not_refused()
    {
        var plan = Draft();

        Assert.Null(plan.PreparedBy);
        plan.Approve(1, Planner, At);

        Assert.Equal(PlanStatus.Approved, plan.Status);
    }

    [Fact]
    public void A_preparer_that_is_not_a_user_id_never_matches_a_decider()
    {
        var plan = Draft();
        plan.RecordPreparer("integration-service");

        plan.Approve(1, Planner, At);

        Assert.Equal(PlanStatus.Approved, plan.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    public void A_blank_or_control_character_preparer_is_refused(string preparer)
    {
        var plan = Draft();

        Assert.Throws<ArgumentException>(() => plan.RecordPreparer(preparer));
        Assert.Null(plan.PreparedBy);
    }

    [Fact]
    public void A_preparer_longer_than_its_column_is_refused_and_one_at_the_bound_is_kept_trimmed()
    {
        var plan = Draft();

        Assert.Throws<ArgumentException>(() => plan.RecordPreparer(new string('x', ProductionPlan.PreparedByMaximumLength + 1)));
        plan.RecordPreparer("  " + new string('x', ProductionPlan.PreparedByMaximumLength) + "  ");

        Assert.Equal(ProductionPlan.PreparedByMaximumLength, plan.PreparedBy!.Length);
    }

    [Fact]
    public void The_stored_replay_keeps_old_self_decisions_and_then_refuses_the_preparer()
    {
        // A rejection recorded before U2-d by the same user who later is the preparer: the replay does not re-check it.
        var plan = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()],
            null, approvedByUserId: null, approvedAtUtc: null, version: 2,
            rejections: [new PlanRejection(1, "eski", Planner, At, 2)],
            preparedBy: Planner.ToString("D"));

        Assert.Equal((2L, Planner.ToString("D")), (plan.Version, plan.PreparedBy));
        Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval,
            Assert.Throws<ManufacturingRuleException>(() => plan.Approve(2, Planner, At)).Code);
    }

    [Fact]
    public void A_stored_preparer_the_rules_refuse_is_a_stored_record_error()
    {
        var refusal = Assert.Throws<ManufacturingRuleException>(() => ProductionPlan.Restore(Guid.NewGuid(), Company,
            WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null, null, null, 1, preparedBy: "  "));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refusal.Code);
    }
}
