using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S19 (B-27, DEC-PRDCT-009): the cost trace is the order's raw data only. Lots follow the planned inputs (an
/// unissued roll is listed with zero kg), consumption is issued minus returned, outputs are net of their reversals,
/// the subcontract owner is the planned input's commercial snapshot, and no time figure is invented. The method code
/// names the calculator's method; no amount is computed here.
/// </summary>
public sealed class ProductionCostTraceBuilderTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000f001");
    private static readonly Guid FirstRoll = Guid.Parse("30000000-0000-0000-0000-00000000f001");
    private static readonly Guid SecondRoll = Guid.Parse("30000000-0000-0000-0000-00000000f002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000f001");
    private static readonly Guid Party = Guid.Parse("50000000-0000-0000-0000-00000000f001");
    private static readonly Guid Account = Guid.Parse("60000000-0000-0000-0000-00000000f001");
    private const string Reason = "Tartım hatası";

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order(bool customerOwnedSecondRoll = false)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
        [
            new PlannedInput(FirstRoll, Kg(1_000m)),
            new PlannedInput(SecondRoll, Kg(500m), commercialContext: customerOwnedSecondRoll
                ? new PlanCommercialContextSnapshotV1(Party, Account, "party-v1", "account-v1")
                : null),
        ]);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void A_fresh_order_lists_every_planned_roll_with_zero_kg_and_no_moment()
    {
        var order = Order();

        var trace = ProductionCostTraceBuilderV1.Build(order);

        Assert.Equal((1, ProductionCostTraceCodesV1.MethodWeightedAverageRawMaterialKg),
            (trace.ContractVersion, trace.MethodCode));
        Assert.Equal((order.Id, Company, order.PlanId, WorkCenter, ManufacturingProcess.Slitting, order.Version),
            (trace.OrderId, trace.CompanyId, trace.PlanId, trace.WorkCenterId, trace.Process, trace.Version));
        Assert.Equal([FirstRoll, SecondRoll], trace.Lots.Select(lot => lot.ParentRollId));
        Assert.Equal([1_000m, 500m], trace.Lots.Select(lot => lot.PlannedKg.Value));
        Assert.All(trace.Lots, lot =>
        {
            Assert.Equal((0m, 0m, 0m, 0, 0), (lot.IssuedKg.Value, lot.ReturnedKg.Value, lot.ConsumedKg.Value,
                lot.IssueCount, lot.ReturnCount));
            Assert.Null(lot.FirstIssuedAtUtc);
            Assert.Null(lot.LastIssuedAtUtc);
        });
        Assert.Equal((0m, 0m, 0m),
            (trace.TotalIssuedKg.Value, trace.TotalReturnedKg.Value, trace.TotalConsumedKg.Value));
        Assert.Empty(trace.Outputs);
        Assert.Equal(new ProductionCostTraceTimelineV1(null, null, null,
            ProductionCostTraceCodesV1.TimeRecordsNotRecorded, ProductionCostTraceCodesV1.TimeRecordsNotRecorded),
            trace.Timeline);
    }

    [Fact]
    public void A_lot_consumes_its_issues_minus_its_returns_and_the_totals_add_the_lots()
    {
        var order = Order();
        var first = order.Issue(FirstRoll, Kg(400m));
        var second = order.Issue(FirstRoll, Kg(250.5m));
        order.Return(FirstRoll, Kg(50.25m));
        order.Issue(SecondRoll, Kg(100m));

        var trace = ProductionCostTraceBuilderV1.Build(order);

        var firstLot = trace.Lots[0];
        Assert.Equal((650.5m, 50.25m, 600.25m, 2, 1),
            (firstLot.IssuedKg.Value, firstLot.ReturnedKg.Value, firstLot.ConsumedKg.Value, firstLot.IssueCount,
                firstLot.ReturnCount));
        Assert.Equal((first.IssuedAtUtc, second.IssuedAtUtc),
            (firstLot.FirstIssuedAtUtc!.Value, firstLot.LastIssuedAtUtc!.Value));
        var secondLot = trace.Lots[1];
        Assert.Equal((100m, 0m, 100m), (secondLot.IssuedKg.Value, secondLot.ReturnedKg.Value,
            secondLot.ConsumedKg.Value));
        Assert.Equal((750.5m, 50.25m, 700.25m),
            (trace.TotalIssuedKg.Value, trace.TotalReturnedKg.Value, trace.TotalConsumedKg.Value));
    }

    [Fact]
    public void Outputs_are_grouped_by_class_in_enum_order_and_counted_net_of_their_reversals()
    {
        var order = Order();
        order.Issue(FirstRoll, Kg(900m));
        order.Declare(FirstRoll, OutputClass.Scrap, Kg(20m));
        var good = order.DeclareOutput(order.Version, Guid.NewGuid(), FirstRoll, OutputClass.GoodProduct, Kg(600m),
            OrderSteps.Next(), PlanOutputLineReference.ClassLine);
        order.Declare(FirstRoll, OutputClass.GoodProduct, Kg(200m));
        order.ReverseOutput(order.Version, Guid.NewGuid(), good.OutputId, Kg(15.5m), Reason, OrderSteps.Next());

        var trace = ProductionCostTraceBuilderV1.Build(order);

        Assert.Equal(
            [
                new ProductionCostTraceOutputV1(OutputClass.GoodProduct, Kg(784.5m), 2, 1),
                new ProductionCostTraceOutputV1(OutputClass.Scrap, Kg(20m), 1, 0),
            ],
            trace.Outputs);
    }

    [Fact]
    public void The_timeline_spans_every_stored_fact_and_names_the_missing_time_records()
    {
        var order = Order();
        var issue = order.Issue(FirstRoll, Kg(300m));
        order.MeasureWip(FirstRoll, Kg(10m));
        var declared = order.DeclareOutput(order.Version, Guid.NewGuid(), FirstRoll, OutputClass.GoodProduct, Kg(100m),
            OrderSteps.Next(), PlanOutputLineReference.ClassLine);
        var reversal = order.ReverseOutput(order.Version, Guid.NewGuid(), declared.OutputId, Kg(5m), Reason,
            OrderSteps.Next());

        var timeline = ProductionCostTraceBuilderV1.Build(order).Timeline;

        Assert.Equal((issue.IssuedAtUtc, reversal.ReversedAtUtc),
            (timeline.FirstFactAtUtc!.Value, timeline.LastFactAtUtc!.Value));
        Assert.Null(timeline.ClosedAtUtc);
        Assert.Equal((ProductionCostTraceCodesV1.TimeRecordsNotRecorded, ProductionCostTraceCodesV1.TimeRecordsNotRecorded),
            (timeline.StoppageCode, timeline.DurationCode));
    }

    [Fact]
    public void A_customer_owned_lot_carries_its_planned_party_and_account_and_a_company_owned_lot_carries_none()
    {
        var order = Order(customerOwnedSecondRoll: true);

        var trace = ProductionCostTraceBuilderV1.Build(order);

        Assert.Equal(((Guid?)null, (Guid?)null), (trace.Lots[0].CommercialPartyId, trace.Lots[0].CommercialPartyAccountId));
        Assert.Equal(((Guid?)Party, (Guid?)Account),
            (trace.Lots[1].CommercialPartyId, trace.Lots[1].CommercialPartyAccountId));
    }

    [Fact]
    public void Building_the_trace_reads_the_order_and_changes_nothing()
    {
        var order = Order();
        order.Issue(FirstRoll, Kg(300m));
        var version = order.Version;
        var issues = order.Issues.Count;

        var first = ProductionCostTraceBuilderV1.Build(order);
        var again = ProductionCostTraceBuilderV1.Build(order);

        Assert.Equal((version, issues), (order.Version, order.Issues.Count));
        Assert.Equal(first.Lots, again.Lots);
        Assert.Equal(first.Timeline, again.Timeline);
    }
}
