using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D66 (3)/(4): the production order opens with the plan's termin, copied once; from then on the order's termin is
/// changed on the order alone. A change moves the order's termin revision and never its version nor the plan's termin;
/// the order shows when its termin differs from the plan's. A stale termin revision, a closed order, no change and a day
/// earlier than the company's Europe/Istanbul business day are refused with nothing changed. The stored termin is restored
/// and checked.
/// </summary>
public sealed class OrderDueDateTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000d001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000d002");
    private static readonly DateTimeOffset At = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateOnly PlanDay = new(2026, 12, 31);
    private static readonly DateOnly OrderDay = new(2027, 1, 15);

    private static Kilograms Kg(decimal value) => new(value);

    private static (ProductionPlan Plan, ProductionOrder Order) Opened()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, Kg(10_000m))]);
        plan.ChangeDueDate(plan.Version, plan.DueDateRevision, PlanDay, orderClosed: false, Today);
        plan.Approve(plan.Version, Planner, At);
        return (plan, ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan));
    }

    [Fact]
    public void The_order_opens_with_the_plans_termin_and_no_difference()
    {
        var (_, order) = Opened();

        Assert.Equal((PlanDay, 0L, PlanDay, false),
            (order.DueDate, order.DueDateRevision, order.PlanDueDate, order.DueDateDiffersFromPlan));
    }

    [Fact]
    public void A_change_moves_the_termin_revision_and_not_the_order_version_nor_the_plans_termin()
    {
        var (plan, order) = Opened();
        var version = order.Version;

        order.ChangeDueDate(0, OrderDay, Today);

        Assert.Equal((OrderDay, 1L, version, true),
            (order.DueDate, order.DueDateRevision, order.Version, order.DueDateDiffersFromPlan));
        Assert.Equal((PlanDay, PlanDay), (order.PlanDueDate, plan.RequestedDueDate));

        // Back to the plan's day: the order keeps its own revision and no longer differs.
        order.ChangeDueDate(1, PlanDay, Today);
        Assert.Equal((PlanDay, 2L, false), (order.DueDate, order.DueDateRevision, order.DueDateDiffersFromPlan));
    }

    [Fact]
    public void The_business_day_itself_is_taken_and_the_day_before_is_refused()
    {
        var (_, order) = Opened();
        AssertRefused(order, 0, Today.AddDays(-1), ManufacturingRuleCodes.DueDateInPast);

        order.ChangeDueDate(0, Today, Today);
        Assert.Equal(Today, order.DueDate);
    }

    [Fact]
    public void A_stale_termin_revision_is_refused_and_nothing_changes()
    {
        var (_, order) = Opened();
        order.ChangeDueDate(0, OrderDay, Today);

        AssertRefused(order, 0, PlanDay, ManufacturingRuleCodes.OrderDueDateChanged);
    }

    [Fact]
    public void The_same_day_again_is_refused_as_no_change() =>
        AssertRefused(Opened().Order, 0, PlanDay, ManufacturingRuleCodes.OrderDueDateNoChange);

    [Fact]
    public void A_closed_order_is_refused_and_nothing_changes()
    {
        var (_, order) = Opened();
        order.Issue(Roll, Kg(10_000m));
        order.Declare(Roll, OutputClass.GoodProduct, Kg(10_000m));
        order.Close(order.Version, Closer, At);

        AssertRefused(order, 0, OrderDay, ManufacturingRuleCodes.OrderDueDateOrderClosed);
    }

    [Fact]
    public void The_stored_termin_is_restored_and_an_inconsistent_one_is_refused()
    {
        var restored = Restore(OrderDay, 3, PlanDay);
        Assert.Equal((OrderDay, 3L, PlanDay, true),
            (restored.DueDate, restored.DueDateRevision, restored.PlanDueDate, restored.DueDateDiffersFromPlan));

        var legacy = Restore(null, 0, null);
        Assert.Equal((null as DateOnly?, 0L, false), (legacy.DueDate, legacy.DueDateRevision, legacy.DueDateDiffersFromPlan));

        foreach (var (day, revision) in new (DateOnly?, long)[] { (null, 1), (OrderDay, -1), (null, -1) })
        {
            var refusal = Assert.Throws<ManufacturingRuleException>(() => Restore(day, revision, PlanDay));
            Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refusal.Code);
        }
    }

    private static ProductionOrder Restore(DateOnly? day, long revision, DateOnly? planDay) =>
        ProductionOrder.Restore(Guid.NewGuid(), Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            null, [new PlannedInput(Roll, Kg(10_000m))], [], [], [], [], null, null, 1,
            dueDate: day, dueDateRevision: revision, planDueDate: planDay);

    private static void AssertRefused(ProductionOrder order, long revision, DateOnly day, string code)
    {
        var before = (order.DueDate, order.DueDateRevision, order.Version, order.Status);

        var refusal = Assert.Throws<ManufacturingRuleException>(() => order.ChangeDueDate(revision, day, Today));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(before, (order.DueDate, order.DueDateRevision, order.Version, order.Status));
    }
}
