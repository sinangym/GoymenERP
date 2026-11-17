using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class OrderReopeningTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static ProductionOrder NewOrder()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ManufacturingProcess.Paint,
            [new PlannedInput(Guid.NewGuid(), new Kilograms(100m))]);
        plan.WithDueDate().Approve(plan.Version, Actor, At);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void Reopening_keeps_the_original_close_header_and_a_new_close_preserves_the_corrected_current_facts()
    {
        var order = NewOrder(); var roll = order.PlannedInputs[0].ParentRollId;
        order.IssueInput(order.Version, Guid.NewGuid(), roll, new Kilograms(100m), At);
        var output = order.DeclareOutput(order.Version, Guid.NewGuid(), roll, OutputClass.GoodProduct, new Kilograms(100m), At, PlanOutputLineReference.ClassLine);
        order.Close(order.Version, Actor, At.AddMinutes(1)); var closedVersion = order.Version;
        var reopened = order.Reopen(closedVersion, Actor, At.AddMinutes(2), "  Synthetic correction  ");
        Assert.Equal((closedVersion, closedVersion + 1, "Synthetic correction"), (reopened.ClosedOrderVersion, reopened.ReopenedOrderVersion, reopened.Reason));
        Assert.Equal(OrderStatus.Open, order.Status); Assert.Null(order.ClosedAtUtc); Assert.Null(order.ClosedByUserId);
        order.ReverseOutput(order.Version, Guid.NewGuid(), output.OutputId, new Kilograms(10m), "Synthetic reduction", At.AddMinutes(3));
        order.DeclareOutput(order.Version, Guid.NewGuid(), roll, OutputClass.GoodProduct, new Kilograms(10m), At.AddMinutes(4), PlanOutputLineReference.ClassLine);
        order.Close(order.Version, Actor, At.AddMinutes(5));
        var restored = ProductionOrder.Restore(order.Id, order.PlanId, order.CompanyId, order.WorkCenterId, order.Process, order.Target,
            order.PlannedInputs, order.Issues, order.Returns, order.Outputs, order.WipMeasurements,
            order.ClosedByUserId, order.ClosedAtUtc, order.Version, outputReversals: order.OutputReversals, reopenings: order.Reopenings);
        Assert.Equal(order.Version, restored.Version); Assert.Equal(OrderStatus.Closed, restored.Status);
        Assert.Equal(reopened, Assert.Single(restored.Reopenings)); Assert.Equal(At.AddMinutes(1), restored.Reopenings[0].ClosedAtUtc);
        Assert.Equal(100m, restored.BalanceOf(roll).OutputOf(OutputClass.GoodProduct).Value);
    }

    [Fact]
    public void Reopening_is_a_versioned_closed_only_reasoned_transition_and_a_refusal_changes_nothing()
    {
        var order = NewOrder();
        Assert.Equal("manufacturing_order_not_closed", Assert.Throws<ManufacturingRuleException>(() => order.Reopen(order.Version, Actor, At, "Synthetic reason")).Code);
        order.Close(order.Version, Actor, At); var version = order.Version;
        Assert.Throws<ManufacturingVersionConflictException>(() => order.Reopen(version - 1, Actor, At, "Synthetic reason"));
        Assert.Throws<ArgumentException>(() => order.Reopen(version, Actor, At, " "));
        Assert.Equal((version, OrderStatus.Closed), (order.Version, order.Status)); Assert.Empty(order.Reopenings);
    }

    [Fact]
    public void A_stored_reopening_cannot_forge_a_gap_or_duplicate_in_its_transition_chain()
    {
        var order = NewOrder(); order.Close(order.Version, Actor, At); order.Reopen(order.Version, Actor, At, "Synthetic reason");
        var row = Assert.Single(order.Reopenings);
        void Restore(IEnumerable<ProductionOrderReopeningV1> rows) => ProductionOrder.Restore(order.Id, order.PlanId, order.CompanyId,
            order.WorkCenterId, order.Process, order.Target, order.PlannedInputs, [], [], [], [], null, null, order.Version, reopenings: rows);
        Assert.Throws<ManufacturingRuleException>(() => Restore([row with { ReopenedOrderVersion = 99 }]));
        Assert.Throws<ManufacturingRuleException>(() => Restore([row, row]));
    }
}
