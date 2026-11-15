using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim F-010a (URT-D29): the termin is entered by hand on the plan. Entering, changing and removing it move the termin
/// revision and never the plan version; a cancelled plan, a closed order, a stale termin revision and no change are refused
/// with nothing changed; no date rule is applied (a past day is the user's entry). The stored termin is restored and checked.
/// </summary>
public sealed class PlanDueDateTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000f001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000f001");
    private static readonly DateTimeOffset At = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DayA = new(2026, 12, 31);
    private static readonly DateOnly DayB = new(2027, 1, 15);

    private static PlannedInput Input() => new(Roll, new Kilograms(1_000m), 1, null);

    private static ProductionPlan Draft() =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()]);

    [Fact]
    public void Entering_changing_and_removing_move_the_termin_revision_and_not_the_plan_version()
    {
        var plan = Draft();

        plan.ChangeDueDate(1, 0, DayA, orderClosed: false);
        Assert.Equal((DayA, 1L, 1L), (plan.RequestedDueDate, plan.DueDateRevision, plan.Version));

        plan.ChangeDueDate(1, 1, DayB, orderClosed: false);
        Assert.Equal((DayB, 2L), (plan.RequestedDueDate, plan.DueDateRevision));

        plan.ChangeDueDate(1, 2, null, orderClosed: false);
        Assert.Null(plan.RequestedDueDate);
        Assert.Equal((3L, 1L), (plan.DueDateRevision, plan.Version));
    }

    [Fact]
    public void An_approved_plan_takes_a_termin_while_its_order_is_open()
    {
        var plan = Draft();
        plan.Approve(1, Guid.NewGuid(), At);

        plan.ChangeDueDate(2, 0, DayA, orderClosed: false);

        Assert.Equal((PlanStatus.Approved, DayA, 2L), (plan.Status, plan.RequestedDueDate, plan.Version));
    }

    [Fact]
    public void A_past_day_is_the_users_entry_and_is_kept()
    {
        var plan = Draft();
        var past = new DateOnly(2020, 1, 1);

        plan.ChangeDueDate(1, 0, past, orderClosed: false);

        Assert.Equal(past, plan.RequestedDueDate);
    }

    [Fact]
    public void A_cancelled_plan_is_refused_and_nothing_changes()
    {
        var plan = Draft();
        plan.Cancel(1, Guid.NewGuid(), At, "gerek kalmadı");

        AssertRefused(plan, plan.Version, 0, DayA, false, ManufacturingRuleCodes.PlanCancelled);
    }

    [Fact]
    public void A_stale_termin_revision_is_refused_and_nothing_changes()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false);

        AssertRefused(plan, 1, 0, DayB, false, ManufacturingRuleCodes.PlanDueDateChanged);
        Assert.Equal(DayA, plan.RequestedDueDate);
    }

    [Fact]
    public void A_closed_order_is_refused_and_nothing_changes() =>
        AssertRefused(Draft(), 1, 0, DayA, true, ManufacturingRuleCodes.PlanDueDateOrderClosed);

    [Fact]
    public void Entering_the_same_day_again_or_removing_no_termin_is_refused_as_no_change()
    {
        AssertRefused(Draft(), 1, 0, null, false, ManufacturingRuleCodes.PlanDueDateNoChange);

        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false);
        AssertRefused(plan, 1, 1, DayA, false, ManufacturingRuleCodes.PlanDueDateNoChange);
    }

    [Fact]
    public void A_stale_plan_version_is_refused_as_a_version_conflict()
    {
        var plan = Draft();

        Assert.Throws<ManufacturingVersionConflictException>(() => plan.ChangeDueDate(2, 0, DayA, orderClosed: false));

        Assert.Equal((null as DateOnly?, 0L), (plan.RequestedDueDate, plan.DueDateRevision));
    }

    [Fact]
    public void A_revision_of_the_plan_keeps_the_termin_and_its_revision()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false);

        // A real revision (the planned quantity changes); an unchanged one is refused as no change.
        plan.Revise(1, WorkCenter, ManufacturingProcess.ColdRolling,
            [new PlannedInput(Roll, new Kilograms(1_200m), 1, null)], null);

        Assert.Equal((DayA, 1L, 2L), (plan.RequestedDueDate, plan.DueDateRevision, plan.Version));
    }

    [Fact]
    public void The_stored_termin_is_restored_and_an_inconsistent_one_is_refused()
    {
        var id = Guid.NewGuid();
        var restored = Restore(id, DayA, 4);
        Assert.Equal((DayA, 4L), (restored.RequestedDueDate, restored.DueDateRevision));
        Assert.Equal(2L, Restore(id, null, 2).DueDateRevision);
        Assert.Equal(0L, Restore(id, null, 0).DueDateRevision);

        foreach (var (day, revision) in new (DateOnly?, long)[] { (DayA, 0), (null, -1), (DayA, -1) })
        {
            var refusal = Assert.Throws<ManufacturingRuleException>(() => Restore(id, day, revision));
            Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refusal.Code);
        }
    }

    private static ProductionPlan Restore(Guid id, DateOnly? day, long revision) =>
        ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null, null, null, 1,
            requestedDueDate: day, dueDateRevision: revision);

    private static void AssertRefused(ProductionPlan plan, long version, long dueDateRevision, DateOnly? day,
        bool orderClosed, string code)
    {
        var before = (plan.RequestedDueDate, plan.DueDateRevision, plan.Version);

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.ChangeDueDate(version, dueDateRevision, day, orderClosed));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(before, (plan.RequestedDueDate, plan.DueDateRevision, plan.Version));
    }
}
