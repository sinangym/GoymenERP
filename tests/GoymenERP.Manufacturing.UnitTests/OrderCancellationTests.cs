using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// F-009 D5 (URT-D33/D34/D35/D39; analysis §5 D5): an order with no net effect left is cancelled — a terminal state —
/// and its approved plan is released; the released plan opens no new order until a minimal Approved→Approved
/// re-approval, which keeps the first approval, refuses the preparer and refuses a Settings profile that changed since
/// the first approval.
/// </summary>
public sealed class OrderCancellationTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000e001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000e001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000e001");
    private static readonly Guid Canceller = Guid.Parse("40000000-0000-0000-0000-00000000e002");
    private static readonly Guid Reapprover = Guid.Parse("40000000-0000-0000-0000-00000000e003");
    private static readonly Guid Preparer = Guid.Parse("40000000-0000-0000-0000-00000000e004");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000e001");
    private static readonly DateTimeOffset ApprovedAt = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CancelledAt = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReapprovedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private const string Reason = "Müşteri siparişi geri çekti";

    private static Kilograms Kg(decimal value) => new(value);

    private static PlanParameterSnapshotV1 Settings(string version = "3", decimal filmMicrons = 80m) =>
        new(Profile, version, PlanParameterSnapshotV1.SettingsSource, ManufacturingProcess.Paint, WorkCenter,
            new DateOnly(2026, 11, 1), null, new DateOnly(2026, 11, 2),
            [new PlanParameterValueV1("filmMicrons", "um", filmMicrons, null)]);

    private static ProductionPlan ApprovedPlan(PlanParameterSnapshotV1? snapshot = null, bool withPreparer = false)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Paint,
            [new PlannedInput(Roll, Kg(1_000m))]);
        if (withPreparer)
        {
            plan.RecordPreparer(Preparer.ToString("D"));
        }

        plan.WithDueDate().Approve(plan.Version, Planner, ApprovedAt, null, snapshot ?? Settings());
        return plan;
    }

    private static (ProductionPlan Plan, ProductionOrder Order) CancelledOrder(
        PlanParameterSnapshotV1? snapshot = null, bool withPreparer = false)
    {
        var plan = ApprovedPlan(snapshot, withPreparer);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Cancel(order.Version, Canceller, CancelledAt, Reason);
        plan.ReleaseCancelledOrder(order.Id);
        return (plan, order);
    }

    [Fact]
    public void An_order_without_facts_is_cancelled_and_its_plan_is_released_for_reapproval()
    {
        var plan = ApprovedPlan();
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        var orderVersion = order.Version;
        var planVersion = plan.Version;

        order.Cancel(orderVersion, Canceller, CancelledAt, "  " + Reason + "  ");
        plan.ReleaseCancelledOrder(order.Id);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(orderVersion + 1, order.Version);
        Assert.Equal((Canceller, CancelledAt, Reason), (order.CancelledByUserId!.Value, order.CancelledAtUtc!.Value,
            order.CancellationReason));
        Assert.Null(plan.OrderId);
        Assert.Equal(planVersion + 1, plan.Version);
        Assert.Equal([new PlanOrderRelease(order.Id, plan.Version)], plan.OrderReleases);
        Assert.True(plan.RequiresReapproval);
        Assert.Equal((PlanStatus.Approved, Planner), (plan.Status, plan.ApprovedByUserId!.Value));
    }

    [Fact]
    public void An_order_whose_issues_were_returned_and_outputs_reversed_in_full_is_cancelled()
    {
        var plan = ApprovedPlan();
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Issue(Roll, Kg(400m));
        var output = order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, Kg(150m), OrderSteps.Next(), PlanOutputLineReference.ClassLine);
        order.ReverseOutput(order.Version, Guid.NewGuid(), output.OutputId, Kg(150m), "Tartım hatası", OrderSteps.Next());
        order.MeasureWip(Roll, Kg(0m));
        order.Return(Roll, Kg(400m));

        order.Cancel(order.Version, Canceller, CancelledAt, Reason);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void An_order_with_an_unreturned_issue_is_not_cancelled()
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());
        order.Issue(Roll, Kg(400m));
        order.Return(Roll, Kg(100m));
        var version = order.Version;

        var refusal = Assert.Throws<ManufacturingRuleException>(() => order.Cancel(version, Canceller, CancelledAt, Reason));

        Assert.Equal(ManufacturingRuleCodes.OrderCancelHasNetEffect, refusal.Code);
        Assert.Equal((OrderStatus.Open, version), (order.Status, order.Version));
        Assert.Null(order.CancelledByUserId);
    }

    [Fact]
    public void An_order_with_a_net_output_is_not_cancelled()
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());
        order.Issue(Roll, Kg(400m));
        var output = order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, Kg(150m), OrderSteps.Next(), PlanOutputLineReference.ClassLine);
        order.ReverseOutput(order.Version, Guid.NewGuid(), output.OutputId, Kg(100m), "Tartım hatası", OrderSteps.Next());
        order.Return(Roll, Kg(350m));

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => order.Cancel(order.Version, Canceller, CancelledAt, Reason));

        Assert.Equal(ManufacturingRuleCodes.OrderCancelHasNetEffect, refusal.Code);
    }

    [Fact]
    public void An_order_whose_latest_wip_reading_is_not_zero_is_not_cancelled()
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());
        order.Issue(Roll, Kg(400m));
        order.Return(Roll, Kg(400m));
        order.MeasureWip(Roll, Kg(25m));

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => order.Cancel(order.Version, Canceller, CancelledAt, Reason));

        Assert.Equal(ManufacturingRuleCodes.OrderCancelHasNetEffect, refusal.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("satır\nsonu")]
    public void A_cancel_without_a_valid_reason_is_refused(string? reason)
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());

        Assert.Throws<ArgumentException>(() => order.Cancel(order.Version, Canceller, CancelledAt, reason));
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void A_cancel_on_a_stale_version_is_refused()
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());

        Assert.Throws<ManufacturingVersionConflictException>(() => order.Cancel(order.Version + 1, Canceller, CancelledAt, Reason));
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void A_cancelled_order_takes_no_new_fact_and_is_not_cancelled_again()
    {
        var (_, order) = CancelledOrder();

        var issue = Assert.Throws<ManufacturingRuleException>(() => order.Issue(Roll, Kg(10m)));
        var again = Assert.Throws<ManufacturingRuleException>(
            () => order.Cancel(order.Version, Canceller, CancelledAt, Reason));

        Assert.Equal(ManufacturingRuleCodes.OrderCancelled, issue.Code);
        Assert.Equal(ManufacturingRuleCodes.OrderCancelled, again.Code);
    }

    [Fact]
    public void A_stored_cancelled_order_restores_with_its_cancel_trace()
    {
        var (plan, order) = CancelledOrder();

        var restored = ProductionOrder.Restore(order.Id, plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, null,
            plan.Inputs, [], [], [], [], null, null, order.Version, cancelledByUserId: Canceller,
            cancelledAtUtc: CancelledAt, cancellationReason: Reason);

        Assert.Equal(OrderStatus.Cancelled, restored.Status);
        Assert.Equal(order.Version, restored.Version);
        Assert.Equal((Canceller, CancelledAt, Reason), (restored.CancelledByUserId!.Value, restored.CancelledAtUtc!.Value,
            restored.CancellationReason));
    }

    [Fact]
    public void A_stored_cancel_trace_with_a_missing_field_is_refused()
    {
        var (plan, order) = CancelledOrder();

        var refusal = Assert.Throws<ManufacturingRuleException>(() => ProductionOrder.Restore(order.Id, plan.Id, Company,
            WorkCenter, ManufacturingProcess.Paint, null, plan.Inputs, [], [], [], [], null, null, order.Version,
            cancelledByUserId: Canceller, cancelledAtUtc: null, cancellationReason: Reason));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refusal.Code);
    }

    [Fact]
    public void A_released_plan_opens_no_order_until_it_is_reapproved()
    {
        var (plan, _) = CancelledOrder();

        var refusal = Assert.Throws<ManufacturingRuleException>(() => ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan));
        Assert.Equal(ManufacturingRuleCodes.PlanReapprovalRequired, refusal.Code);

        plan.Reapprove(plan.Version, Reapprover, ReapprovedAt, Settings(), null);
        var next = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);

        Assert.Equal(next.Id, plan.OrderId);
        Assert.Equal(OrderStatus.Open, next.Status);
    }

    [Fact]
    public void A_reapproval_keeps_the_first_approval_and_records_itself_at_the_next_version()
    {
        var (plan, _) = CancelledOrder();
        var version = plan.Version;
        var firstSnapshot = plan.ParameterSnapshot;

        plan.Reapprove(version, Reapprover, ReapprovedAt, Settings(), null);

        Assert.Equal(version + 1, plan.Version);
        Assert.False(plan.RequiresReapproval);
        Assert.Equal([new PlanReapproval(1, Reapprover, ReapprovedAt, version + 1)], plan.Reapprovals);
        Assert.Equal((Planner, ApprovedAt), (plan.ApprovedByUserId!.Value, plan.ApprovedAtUtc!.Value));
        Assert.Same(firstSnapshot, plan.ParameterSnapshot);
    }

    [Fact]
    public void A_plan_that_was_not_released_is_not_reapproved()
    {
        var plan = ApprovedPlan();
        var withOrder = ApprovedPlan();
        ProductionOrder.FromApprovedPlan(Guid.NewGuid(), withOrder);

        var notRequired = Assert.Throws<ManufacturingRuleException>(
            () => plan.Reapprove(plan.Version, Reapprover, ReapprovedAt, Settings(), null));
        var orderExists = Assert.Throws<ManufacturingRuleException>(
            () => withOrder.Reapprove(withOrder.Version, Reapprover, ReapprovedAt, Settings(), null));

        Assert.Equal(ManufacturingRuleCodes.PlanReapprovalNotRequired, notRequired.Code);
        Assert.Equal(ManufacturingRuleCodes.PlanOrderExists, orderExists.Code);
    }

    [Fact]
    public void The_preparer_does_not_reapprove()
    {
        var (plan, _) = CancelledOrder(withPreparer: true);
        var version = plan.Version;

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.Reapprove(version, Preparer, ReapprovedAt, Settings(), null));

        Assert.Equal(ManufacturingRuleCodes.PlanSelfApproval, refusal.Code);
        Assert.Equal(version, plan.Version);
        Assert.True(plan.RequiresReapproval);
    }

    [Fact]
    public void A_changed_or_missing_settings_profile_refuses_the_reapproval()
    {
        var (plan, _) = CancelledOrder();
        var version = plan.Version;

        var newerVersion = Assert.Throws<ManufacturingRuleException>(
            () => plan.Reapprove(version, Reapprover, ReapprovedAt, Settings(version: "4"), null));
        var newerValue = Assert.Throws<ManufacturingRuleException>(
            () => plan.Reapprove(version, Reapprover, ReapprovedAt, Settings(filmMicrons: 120m), null));
        var missing = Assert.Throws<ManufacturingRuleException>(
            () => plan.Reapprove(version, Reapprover, ReapprovedAt, null, null));

        Assert.All([newerVersion, newerValue, missing],
            refusal => Assert.Equal(ManufacturingRuleCodes.PlanReapprovalSettingsChanged, refusal.Code));
        Assert.Equal(version, plan.Version);
        Assert.Empty(plan.Reapprovals);
    }

    [Fact]
    public void Same_settings_ignores_the_selection_date_but_not_the_profile_identity()
    {
        var stored = Settings();
        var selectedLater = new PlanParameterSnapshotV1(Profile, "3", PlanParameterSnapshotV1.SettingsSource,
            ManufacturingProcess.Paint, WorkCenter, new DateOnly(2026, 11, 1), null, new DateOnly(2026, 11, 9),
            [new PlanParameterValueV1("filmMicrons", "um", 80m, null)]);
        var otherProfile = new PlanParameterSnapshotV1(Guid.Parse("50000000-0000-0000-0000-00000000e002"), "3",
            PlanParameterSnapshotV1.SettingsSource, ManufacturingProcess.Paint, WorkCenter, new DateOnly(2026, 11, 1),
            null, new DateOnly(2026, 11, 2), [new PlanParameterValueV1("filmMicrons", "um", 80m, null)]);

        Assert.True(PlanParameterSnapshotV1.SameSettingsAs(stored, selectedLater));
        Assert.False(PlanParameterSnapshotV1.SameSettingsAs(stored, otherProfile));
        Assert.False(PlanParameterSnapshotV1.SameSettingsAs(null, stored));
        Assert.False(PlanParameterSnapshotV1.SameSettingsAs(stored, null));
    }

    [Fact]
    public void A_stored_release_and_reapproval_replay_to_the_same_plan()
    {
        var (plan, _) = CancelledOrder();
        plan.Reapprove(plan.Version, Reapprover, ReapprovedAt, Settings(), null);

        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, plan.Inputs, null,
            Planner, ApprovedAt, plan.Version, parameterSnapshot: plan.ParameterSnapshot,
            requestedDueDate: plan.RequestedDueDate, dueDateRevision: plan.DueDateRevision,
            orderReleases: plan.OrderReleases, reapprovals: plan.Reapprovals);

        Assert.Equal(plan.Version, restored.Version);
        Assert.Equal(plan.OrderReleases, restored.OrderReleases);
        Assert.Equal(plan.Reapprovals, restored.Reapprovals);
        Assert.False(restored.RequiresReapproval);
        Assert.Null(restored.OrderId);
    }

    [Fact]
    public void A_stored_release_still_waiting_restores_a_plan_that_requires_reapproval()
    {
        var (plan, _) = CancelledOrder();

        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, plan.Inputs, null,
            Planner, ApprovedAt, plan.Version, parameterSnapshot: plan.ParameterSnapshot,
            requestedDueDate: plan.RequestedDueDate, dueDateRevision: plan.DueDateRevision,
            orderReleases: plan.OrderReleases);

        Assert.True(restored.RequiresReapproval);
        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => ProductionOrder.FromApprovedPlan(Guid.NewGuid(), restored));
        Assert.Equal(ManufacturingRuleCodes.PlanReapprovalRequired, refusal.Code);
    }
    [Fact]
    public void A_released_plan_changes_labels_and_replays_the_revision_between_release_and_reapproval()
    {
        var (plan, cancelledOrder) = CancelledOrder();
        var originalInputs = plan.Inputs.ToArray();
        var firstSnapshot = plan.ParameterSnapshot;
        var newRoll = Guid.NewGuid();
        plan.Revise(plan.Version, WorkCenter, ManufacturingProcess.Paint,
            [new PlannedInput(newRoll, Kg(800m), 17)], null);
        var revision = new PlanRevisionContent(WorkCenter, plan.Inputs.ToArray(), null, PlanVersion: plan.Version);
        plan.Reapprove(plan.Version, Reapprover, ReapprovedAt, Settings(), null);
        var secondOrder = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint,
            originalInputs, null, Planner, ApprovedAt, plan.Version, revisions: [revision],
            parameterSnapshot: firstSnapshot, orderId: secondOrder.Id, requestedDueDate: plan.RequestedDueDate,
            dueDateRevision: plan.DueDateRevision, orderReleases: plan.OrderReleases, reapprovals: plan.Reapprovals);

        Assert.Equal(plan.Version, restored.Version);
        Assert.Equal(newRoll, Assert.Single(restored.Inputs).ParentRollId);
        Assert.Equal(Roll, Assert.Single(cancelledOrder.PlannedInputs).ParentRollId);
        Assert.Equal(newRoll, Assert.Single(secondOrder.PlannedInputs).ParentRollId);
        Assert.Equal((Planner, ApprovedAt, firstSnapshot),
            (restored.ApprovedByUserId, restored.ApprovedAtUtc, restored.ParameterSnapshot));
        Assert.Equal(plan.OrderReleases, restored.OrderReleases);
        Assert.Equal(plan.Reapprovals, restored.Reapprovals);
        Assert.Equal(1, restored.RevisionCount);
    }

    [Fact]
    public void A_released_plan_refuses_changes_of_owner_or_its_original_fixed_content_without_mutating()
    {
        var (plan, _) = CancelledOrder();
        var before = plan.Version;
        var commercial = new PlanCommercialContextSnapshotV1(Guid.NewGuid(), Guid.NewGuid(), "1", "1");
        var changedOwner = Assert.Throws<ManufacturingRuleException>(() => plan.Revise(before, WorkCenter,
            ManufacturingProcess.Paint, [new PlannedInput(Guid.NewGuid(), Kg(800m), 17, commercialContext: commercial)], null));
        var changedWorkCenter = Assert.Throws<ManufacturingRuleException>(() => plan.Revise(before, Guid.NewGuid(),
            ManufacturingProcess.Paint, [new PlannedInput(Guid.NewGuid(), Kg(800m), 17)], null));

        Assert.Equal(ManufacturingRuleCodes.PlanSalesOrderPartyMismatch, changedOwner.Code);
        Assert.Equal(ManufacturingRuleCodes.PlanReapprovalSettingsChanged, changedWorkCenter.Code);
        Assert.Equal((before, 0, Roll), (plan.Version, plan.RevisionCount, Assert.Single(plan.Inputs).ParentRollId));
    }

}
