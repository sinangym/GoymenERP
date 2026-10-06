using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// ÜRÜN #280 P3-1: the fact collections were the live lists behind the order, so a cast could add
/// or clear facts past the duplicate, roll and closed-order rules. Every view is now read-only.
/// Also pins the plan → order behaviour that is kept on purpose until Business decides it.
/// </summary>
public sealed class ReadOnlyViewTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionPlan ApprovedPlan()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(MasterRoll, Kg(10_000m))]);
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return plan;
    }

    [Fact]
    public void The_fact_views_cannot_be_used_to_bypass_the_order_rules()
    {
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), ApprovedPlan());
        var issue = order.Issue(MasterRoll, Kg(10_000m));
        var back = order.Return(MasterRoll, Kg(100m));
        var output = order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        var wip = order.MeasureWip(MasterRoll, Kg(200m));

        Assert.False(order.Issues is List<InputIssue>);
        Assert.False(order.Returns is List<InputReturn>);
        Assert.False(order.Outputs is List<OutputDeclaration>);
        Assert.False(order.WipMeasurements is List<WipMeasurement>);
        Assert.False(order.PlannedInputs is List<PlannedInput>);

        Assert.Throws<NotSupportedException>(() => ((IList<InputIssue>)order.Issues).Add(issue));
        Assert.Throws<NotSupportedException>(() => ((IList<InputReturn>)order.Returns).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OutputDeclaration>)order.Outputs).Add(output));
        Assert.Throws<NotSupportedException>(() => ((IList<WipMeasurement>)order.WipMeasurements).Remove(wip));
        Assert.Throws<NotSupportedException>(
            () => ((IList<PlannedInput>)order.PlannedInputs).Add(new PlannedInput(Guid.NewGuid(), Kg(1m))));

        // Nothing changed behind the views.
        Assert.Equal(new[] { issue }, order.Issues);
        Assert.Equal(new[] { back }, order.Returns);
        Assert.Equal(new[] { output }, order.Outputs);
        Assert.Equal(new[] { wip }, order.WipMeasurements);
        Assert.Single(order.PlannedInputs);
    }

    [Fact]
    public void The_plan_inputs_and_a_balance_are_read_only_too()
    {
        var plan = ApprovedPlan();
        Assert.False(plan.Inputs is List<PlannedInput>);
        Assert.Throws<NotSupportedException>(() => ((IList<PlannedInput>)plan.Inputs).Clear());
        Assert.Single(plan.Inputs);

        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        var balance = order.BalanceOf(MasterRoll);

        Assert.False(balance.OutputsByClass is Dictionary<OutputClass, Kilograms>);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<OutputClass, Kilograms>)balance.OutputsByClass)[OutputClass.Scrap] = Kg(300m));
        Assert.Equal(Kg(0m), balance.OutputOf(OutputClass.Scrap));
    }

    [Fact]
    public void One_approved_plan_can_still_open_more_than_one_order_until_Business_decides()
    {
        // Open question (#66 5728934213, same principle as Purchasing's request → several orders):
        // §7.3 does not say whether a plan becomes one order or several partial ones. The current
        // behaviour is kept and pinned; it changes only by that decision. The physical guard is
        // Inventory's: a parent roll cannot be issued beyond the stock its label holds.
        var plan = ApprovedPlan();

        var first = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        var second = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);

        Assert.Equal(plan.Id, first.PlanId);
        Assert.Equal(plan.Id, second.PlanId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void An_order_keeps_the_plans_exact_immutable_commercial_context_while_legacy_null_stays_null()
    {
        var commercial = new PlanCommercialContextSnapshotV1(
            Guid.Parse("50000000-0000-0000-0000-000000000005"),
            Guid.Parse("60000000-0000-0000-0000-000000000006"),
            " party:v01 ",
            "account:v02/α");
        var commercialPlan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(MasterRoll, Kg(9_000m), commercialContext: commercial)]);
        commercialPlan.Approve(commercialPlan.Version, Planner, DateTimeOffset.UnixEpoch);
        var legacyPlan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Guid.NewGuid(), Kg(1_000m))]);
        legacyPlan.Approve(legacyPlan.Version, Planner, DateTimeOffset.UnixEpoch);

        var commercialOrder = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), commercialPlan);
        var legacyOrder = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), legacyPlan);

        Assert.Same(commercial, commercialOrder.PlannedInputs[0].CommercialContext);
        Assert.Equal((" party:v01 ", "account:v02/α"),
            (commercialOrder.PlannedInputs[0].CommercialContext!.PartyVersion,
                commercialOrder.PlannedInputs[0].CommercialContext!.AccountVersion));
        Assert.Null(legacyOrder.PlannedInputs[0].CommercialContext);
    }
}
