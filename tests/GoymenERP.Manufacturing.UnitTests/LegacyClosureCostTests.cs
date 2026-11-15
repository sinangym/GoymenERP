using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D52 (1) (SINAN_KARARLARI_2026-11-14_URETIM_EK4): the cost of an order closed before URT-D41 with output above
/// input reads "hesaplanamadı — çıkan giren kg'dan fazla" and gives no amount; the order is measured as the close and
/// the URT-D42 (3) report measure it (<see cref="ProductionOrder.HasOutputAboveInput"/>) and is neither reopened nor
/// changed. An open order stays pending and a close that reconciles is costed as before.
/// </summary>
public sealed class LegacyClosureCostTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-1111111d5201");
    private static readonly Guid WorkCenter = Guid.Parse("33333333-3333-4333-8333-3333333d5201");
    private static readonly Guid Planner = Guid.Parse("44444444-4444-4444-8444-4444444d5201");
    private static readonly Guid Closer = Guid.Parse("44444444-4444-4444-8444-4444444d5202");
    private static readonly Guid Profile = Guid.Parse("55555555-5555-4555-8555-5555555d5201");
    private static readonly Guid Roll = Guid.Parse("66666666-6666-4666-8666-6666666d5201");
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-0000000d520a");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-0000000d520a");
    private static readonly DateOnly CloseDay = new(2026, 11, 12);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private const string DecidedText = "hesaplanamadı — çıkan giren kg'dan fazla";

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionReceiptCostInput Input(bool closed = true, bool outputAboveInput = false) =>
        new(closed,
            [new IssueCostInput(IssueA, Kg(1_000m), IssueCostReadStatus.Value, 40m)],
            [],
            Kg(900m),
            Kg(50m),
            Kg(0m),
            [new ScrapWeighingInput(ScrapA, Kg(0m), ScrapPriceInput.Missing)],
            Kg(0m),
            ScrapCostSettingStatus.On,
            NonKgInputUse.None,
            outputAboveInput);

    private static ProductionOrder OpenOrder()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, Kg(10_000m))]);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static ProductionOrder Realized(decimal good)
    {
        var order = OpenOrder();
        order.Issue(Roll, Kg(1_000m));
        order.Declare(Roll, OutputClass.Scrap, Kg(30m));
        order.Declare(Roll, OutputClass.GoodProduct, Kg(good));
        return order;
    }

    /// <summary>A close stored under the earlier rule (the earlier ± 2 %), rebuilt the way the store rebuilds it.</summary>
    private static ProductionOrder StoredClose(ProductionOrder open)
    {
        var catalog = new ClosureTolerancePolicySnapshotV1(open.CompanyId, Profile, 0, open.Process, 5m, true,
            new DateOnly(2026, 11, 1), null, CloseDay, 2m, ClosureTolerancePolicySnapshotV1.CatalogSource);
        return ProductionOrder.Restore(open.Id, open.PlanId, open.CompanyId, open.WorkCenterId, open.Process,
            open.Target, open.PlannedInputs, open.Issues, open.Returns, open.Outputs, open.WipMeasurements, Closer, CloseAt,
            open.Version + 1, catalog);
    }

    [Fact]
    public void A_cost_input_marked_closed_with_output_above_input_gives_no_amount()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(outputAboveInput: true));

        Assert.Equal(ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.OutputExceedsInput), result);
        Assert.Equal("output_exceeds_input", ProductionReceiptCostCodesV1.OutputExceedsInput);
        Assert.Null(result.CostPerKg);
    }

    [Fact]
    public void An_open_order_stays_pending_whatever_the_mark()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(closed: false, outputAboveInput: true));

        Assert.Equal(ProductionReceiptCostResult.Pending(ProductionReceiptCostCodesV1.OrderNotClosed), result);
    }

    [Fact]
    public void An_order_closed_with_output_above_input_reads_the_decided_text_and_is_not_changed()
    {
        // 15 kg more output than input, inside the earlier tolerance: closed before URT-D41 without approval.
        var closed = StoredClose(Realized(985m));
        var version = closed.Version;

        var state = LegacyClosureCostV1.StateOf(closed);
        var result = ProductionReceiptCostCalculatorV1.Calculate(LegacyClosureCostV1.WithClosureMeasure(Input(), closed));

        Assert.True(LegacyClosureCostV1.IsOutputAboveInputClosure(closed));
        Assert.Equal(new LegacyClosureCostStateV1(ProductionReceiptCostStatus.Unreadable,
            ProductionReceiptCostCodesV1.OutputExceedsInput, DecidedText), state);
        Assert.Equal(ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.OutputExceedsInput), result);
        Assert.Equal((OrderStatus.Closed, version), (closed.Status, closed.Version));
    }

    [Fact]
    public void A_close_that_reconciles_and_an_open_order_are_not_marked()
    {
        var reconciled = Realized(970m);
        reconciled.Close(reconciled.Version, Closer, CloseAt);
        var open = Realized(985m);

        Assert.Null(LegacyClosureCostV1.StateOf(reconciled));
        Assert.Null(LegacyClosureCostV1.StateOf(open));
        Assert.False(LegacyClosureCostV1.WithClosureMeasure(Input(), reconciled).ClosedWithOutputAboveInput);
        Assert.False(LegacyClosureCostV1.WithClosureMeasure(Input(), open).ClosedWithOutputAboveInput);
        // A mark the caller already set is kept.
        Assert.True(LegacyClosureCostV1.WithClosureMeasure(Input(outputAboveInput: true), reconciled)
            .ClosedWithOutputAboveInput);
    }

    [Fact]
    public void The_cost_text_is_the_decided_turkish_text_from_its_resource_file()
    {
        Assert.Equal(DecidedText, LegacyClosureCostTextsV1.OutputExceedsInput);
    }
}
