using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim F-010a (URT-D29) with URT-D66: the termin is entered by hand on the plan. Entering, changing and removing it move
/// the termin revision and never the plan version; a cancelled plan, a closed order, a stale termin revision and no change
/// are refused with nothing changed. URT-D66: a day earlier than the company's Europe/Istanbul business day is refused, a
/// plan without a termin is not approved (the last approval refusal) and an approved plan's termin is changed, never
/// removed. The stored termin is restored and checked. URT-D70: approval refuses a termin before the Europe/Istanbul
/// business day of the approval instant; the termin day itself is approved.
/// </summary>
public sealed class PlanDueDateTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000f001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000f001");
    private static readonly DateTimeOffset At = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateOnly DayA = new(2026, 12, 31);
    private static readonly DateOnly DayB = new(2027, 1, 15);

    private static PlannedInput Input() => new(Roll, new Kilograms(1_000m), 1, null);

    private static ProductionPlan Draft() =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()]);

    [Fact]
    public void Entering_changing_and_removing_move_the_termin_revision_and_not_the_plan_version()
    {
        var plan = Draft();

        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        Assert.Equal((DayA, 1L, 1L), (plan.RequestedDueDate, plan.DueDateRevision, plan.Version));

        plan.ChangeDueDate(1, 1, DayB, orderClosed: false, Today);
        Assert.Equal((DayB, 2L), (plan.RequestedDueDate, plan.DueDateRevision));

        plan.ChangeDueDate(1, 2, null, orderClosed: false, Today);
        Assert.Null(plan.RequestedDueDate);
        Assert.Equal((3L, 1L), (plan.DueDateRevision, plan.Version));
    }

    [Fact]
    public void An_approved_plan_changes_its_termin_while_its_order_is_open()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        plan.Approve(1, Guid.NewGuid(), At);

        plan.ChangeDueDate(2, 1, DayB, orderClosed: false, Today);

        Assert.Equal((PlanStatus.Approved, DayB, 2L, 2L),
            (plan.Status, plan.RequestedDueDate, plan.DueDateRevision, plan.Version));
    }

    [Fact]
    public void A_plan_without_a_termin_is_not_approved_and_nothing_changes()
    {
        var plan = Draft();

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(1, Guid.NewGuid(), At));

        Assert.Equal(ManufacturingRuleCodes.PlanDueDateRequired, refusal.Code);
        Assert.Equal((PlanStatus.Draft, 1L), (plan.Status, plan.Version));
    }

    [Fact]
    public void Approval_refuses_a_termin_before_the_Istanbul_business_day_of_the_approval_and_nothing_changes()
    {
        // URT-D70: the termin was valid when entered; by the approval instant its day has passed.
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        var dayAfter = new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(1, Guid.NewGuid(), dayAfter));

        Assert.Equal(ManufacturingRuleCodes.DueDateInPast, refusal.Code);
        Assert.Equal((PlanStatus.Draft, 1L, DayA, 1L),
            (plan.Status, plan.Version, plan.RequestedDueDate, plan.DueDateRevision));

        // Moving the termin forward lets the same approval through.
        plan.ChangeDueDate(1, 1, DayB, orderClosed: false, ManufacturingBusinessDay.DateOf(dayAfter));
        plan.Approve(1, Guid.NewGuid(), dayAfter);
        Assert.Equal(PlanStatus.Approved, plan.Status);
    }

    [Fact]
    public void Approval_on_the_termin_day_itself_is_taken_and_the_Istanbul_day_decides()
    {
        // 31 Dec 20:59 UTC is still 31 Dec in Istanbul (UTC+3); 21:00 UTC is already 1 Jan.
        var onTheDay = Draft();
        onTheDay.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        onTheDay.Approve(1, Guid.NewGuid(), new DateTimeOffset(2026, 12, 31, 20, 59, 0, TimeSpan.Zero));
        Assert.Equal(PlanStatus.Approved, onTheDay.Status);

        var nextDay = Draft();
        nextDay.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => nextDay.Approve(1, Guid.NewGuid(), new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero)));
        Assert.Equal(ManufacturingRuleCodes.DueDateInPast, refusal.Code);
        Assert.Equal(PlanStatus.Draft, nextDay.Status);
    }

    [Fact]
    public void A_past_termin_keeps_the_version_conflict_of_a_stale_plan()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);

        Assert.Throws<ManufacturingVersionConflictException>(
            () => plan.Approve(2, Guid.NewGuid(), new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }

    [Fact]
    public void Approval_without_a_termin_keeps_the_version_conflict_of_a_stale_plan()
    {
        // The termin rule is the last refusal: an earlier rule keeps its own answer.
        var plan = Draft();

        Assert.Throws<ManufacturingVersionConflictException>(() => plan.Approve(2, Guid.NewGuid(), At));
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }

    [Fact]
    public void An_approved_plans_termin_is_not_removed_and_nothing_changes()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        plan.Approve(1, Guid.NewGuid(), At);

        AssertRefused(plan, 2, 1, null, false, ManufacturingRuleCodes.PlanDueDateRequired);
        Assert.Equal(DayA, plan.RequestedDueDate);
    }

    [Fact]
    public void A_day_before_the_business_day_is_refused_and_the_business_day_itself_is_taken()
    {
        AssertRefused(Draft(), 1, 0, Today.AddDays(-1), false, ManufacturingRuleCodes.DueDateInPast);

        var plan = Draft();
        plan.ChangeDueDate(1, 0, Today, orderClosed: false, Today);
        Assert.Equal(Today, plan.RequestedDueDate);
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
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);

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
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);
        AssertRefused(plan, 1, 1, DayA, false, ManufacturingRuleCodes.PlanDueDateNoChange);
    }

    [Fact]
    public void A_stale_plan_version_is_refused_as_a_version_conflict()
    {
        var plan = Draft();

        Assert.Throws<ManufacturingVersionConflictException>(
            () => plan.ChangeDueDate(2, 0, DayA, orderClosed: false, Today));

        Assert.Equal((null as DateOnly?, 0L), (plan.RequestedDueDate, plan.DueDateRevision));
    }

    [Fact]
    public void A_revision_of_the_plan_keeps_the_termin_and_its_revision()
    {
        var plan = Draft();
        plan.ChangeDueDate(1, 0, DayA, orderClosed: false, Today);

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

    [Fact]
    public void The_business_day_is_the_Istanbul_calendar_day_of_the_instant()
    {
        // 21:30 UTC is already the next day in Istanbul (UTC+3); 20:59 UTC is still the same day.
        Assert.Equal(new DateOnly(2026, 11, 13),
            ManufacturingBusinessDay.DateOf(new DateTimeOffset(2026, 11, 12, 21, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 12),
            ManufacturingBusinessDay.DateOf(new DateTimeOffset(2026, 11, 12, 20, 59, 0, TimeSpan.Zero)));
    }

    private static ProductionPlan Restore(Guid id, DateOnly? day, long revision) =>
        ProductionPlan.Restore(id, Company, WorkCenter, ManufacturingProcess.ColdRolling, [Input()], null, null, null, 1,
            requestedDueDate: day, dueDateRevision: revision);

    private static void AssertRefused(ProductionPlan plan, long version, long dueDateRevision, DateOnly? day,
        bool orderClosed, string code)
    {
        var before = (plan.RequestedDueDate, plan.DueDateRevision, plan.Version, plan.Status);

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.ChangeDueDate(version, dueDateRevision, day, orderClosed, Today));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(before, (plan.RequestedDueDate, plan.DueDateRevision, plan.Version, plan.Status));
    }
}
