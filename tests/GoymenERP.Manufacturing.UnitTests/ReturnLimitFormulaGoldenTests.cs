using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-SC36 formula golden of the return limit (URT-D95): limit = issued − previous returns − net output (every class)
/// − the latest WIP reading. URT-D102: a roll process (slitting, cold rolling, tube/profile, cut-to-length) measures it
/// on each parent roll; paint measures it on the order total. The numbers are synthetic; each expected limit is this
/// file's own arithmetic of that formula, and the domain must refuse one gram above it and accept exactly it.
/// </summary>
public sealed class ReturnLimitFormulaGoldenTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-0000000036d0");
    private static readonly Guid RollA = Guid.Parse("30000000-0000-0000-0000-0000000036d1");
    private static readonly Guid RollB = Guid.Parse("30000000-0000-0000-0000-0000000036d2");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-0000000036d1");
    private const decimal OneGram = 0.001m;

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order(ManufacturingProcess process, params Guid[] rolls)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, process,
            rolls.Select(roll => new PlannedInput(roll, Kg(10_000m))));
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static string Refused(ProductionOrder order, Guid roll, decimal mass) =>
        Assert.Throws<ManufacturingRuleException>(() => order.Return(roll, Kg(mass))).Code;

    [Theory]
    // issued, previous return, good, second quality, scrap, WIP → limit = issued − return − Σ output − WIP
    [InlineData(ManufacturingProcess.Slitting, 1_000, 100, 400, 50, 50, 150, 250)]
    [InlineData(ManufacturingProcess.ColdRolling, 2_000, 0, 1_200, 0, 100, 300, 400)]
    [InlineData(ManufacturingProcess.TubeProfile, 1_500, 250, 800, 100, 0, 0, 350)]
    [InlineData(ManufacturingProcess.CutToLength, 800, 50, 500, 0, 25, 200, 25)]
    public void A_roll_process_bounds_the_return_on_the_parent_roll_by_the_formula_golden(
        ManufacturingProcess process, int issued, int previousReturn, int good, int secondQuality, int scrap, int wip,
        int expectedLimit)
    {
        Assert.Equal(expectedLimit, issued - previousReturn - good - secondQuality - scrap - wip);
        var order = Order(process, RollA, RollB);
        order.Issue(RollA, Kg(issued));
        order.Issue(RollB, Kg(5_000m));
        if (previousReturn > 0)
        {
            order.Return(RollA, Kg(previousReturn));
        }

        if (good > 0)
        {
            order.Declare(RollA, OutputClass.GoodProduct, Kg(good));
        }

        if (secondQuality > 0)
        {
            order.Declare(RollA, OutputClass.SecondQuality, Kg(secondQuality));
        }

        if (scrap > 0)
        {
            order.Declare(RollA, OutputClass.Scrap, Kg(scrap));
        }

        order.MeasureWip(RollA, Kg(wip));

        // RollB's large unconsumed input never lifts RollA's bound (per parent roll, URT-D102).
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, expectedLimit + OneGram));
        order.Return(RollA, Kg(expectedLimit));
        Assert.Equal(0m, order.BalanceOf(RollA).Unexplained);
    }

    [Fact]
    public void Paint_uses_the_order_total_including_output_reversals_and_the_last_WIP_reading()
    {
        // URT-D108/D109: 2 000 issued - 100 previous returns - (900 + 800 - 100 reversed) - 100 WIP = 200.
        var order = Order(ManufacturingProcess.Paint, RollA, RollB);
        order.Issue(RollA, Kg(1000m));
        order.Issue(RollB, Kg(1000m));
        order.Return(RollB, Kg(100m));
        order.DeclareOutput(order.Version, Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(900m),
            DateTimeOffset.UnixEpoch, PlanOutputLineReference.ClassLine);
        var output = Guid.NewGuid();
        order.DeclareOutput(order.Version, output, RollB, OutputClass.GoodProduct, Kg(800m),
            DateTimeOffset.UnixEpoch, PlanOutputLineReference.ClassLine);
        order.ReverseOutput(order.Version, Guid.NewGuid(), output, Kg(100m), "SENTETIK paint correction",
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        order.MeasureWip(RollA, Kg(150m));
        order.MeasureWip(RollA, Kg(100m));
        Assert.Equal(200m, order.TotalBalance().Unexplained);
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 200m + OneGram));
        // RollA alone has no unconsumed kg; painting measures the order total and allows this 200 kg return.
        order.Return(RollA, Kg(200m));
        Assert.Equal(0m, order.TotalBalance().Unexplained);
    }

    [Fact]
    public void A_paint_order_bounds_the_return_on_the_order_total_by_the_formula_golden()
    {
        // Order total: issued 1 000 + 1 000 − returned 100 − output (900 + 700 + 50) − WIP 120 = 130.
        var order = Order(ManufacturingProcess.Paint, RollA, RollB);
        order.Issue(RollA, Kg(1_000m));
        order.Issue(RollB, Kg(1_000m));
        order.Return(RollB, Kg(100m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(900m));
        order.Declare(RollB, OutputClass.GoodProduct, Kg(700m));
        order.Declare(RollB, OutputClass.Scrap, Kg(50m));
        order.MeasureWip(RollA, Kg(120m));
        const decimal expectedLimit = 1_000m + 1_000m - 100m - (900m + 700m + 50m) - 120m;

        Assert.Equal(130m, expectedLimit);
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollB, expectedLimit + OneGram));
        order.Return(RollB, Kg(expectedLimit));
        Assert.Equal(0m, order.TotalBalance().Unexplained);
    }
}
