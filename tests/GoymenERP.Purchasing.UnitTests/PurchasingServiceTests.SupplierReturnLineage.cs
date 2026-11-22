using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-8 (SAT-D433, SAT-D441, SAT-D444, SAT-D445): a supplier return that names a source label other than the receipt's own label.
/// The gate closed keeps the earlier 503; open, the lineage decides between a lineage return (only this supplier's receipts) and
/// a quantity return (any other lineage). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid LineageLabel = Guid.Parse("88888888-8888-4888-8888-000000000001");
    private static readonly Guid OtherSupplier = Guid.Parse("88888888-8888-4888-8888-000000000002");

    private static CreatePurchaseSupplierReturnV1 LineageReturnRequest() => ReturnRequest() with
    {
        ExpectedPurchaseOrderVersion = null, UndoQuantity = null, ReplacementExpected = false, SourceLabelId = LineageLabel,
    };

    [Fact]
    public async Task With_the_golden_gate_closed_a_source_label_return_keeps_the_earlier_503_and_reads_nothing()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var lineage = new LineageFake();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnPolicy: new PurchaseSupplierReturnPolicyV1(false, LineageGoldenAccepted: false))
            .CreateSupplierReturnAsync(order, LineageReturnRequest(), Metadata("lineage-gate-closed")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (refusal.Status, refusal.Code));
        Assert.Equal((0, 0), (lineage.Calls, returns.NewWrites));
    }

    [Fact]
    public async Task The_purchasing_gate_is_open_in_this_train()
    {
        Assert.True(PurchaseSupplierReturnLineageGoldenV1.SupplierReturnLineageGoldenAccepted);
        Assert.True(PurchaseSupplierReturnPolicyV1.Default.LineageGoldenAccepted);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Without_a_wired_lineage_port_a_source_label_return_is_503_and_nothing_is_recorded()
    {
        var (world, order, returns) = await ReturnWorldAsync();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(returns: returns).CreateSupplierReturnAsync(order, LineageReturnRequest(), Metadata("lineage-no-port")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (refusal.Status, refusal.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task A_split_label_of_the_named_receipt_is_a_lineage_return_recorded_with_its_source_label_and_mode()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        returns.Suppliers[request.GoodsAcceptanceId] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "1", "1")] };

        var answer = await world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-split"));

        Assert.Equal((LineageLabel, "Lineage"), (answer.Return.SourceLabelId, answer.Return.LineageMode));
        Assert.Equal((LineageLabel, PurchaseSupplierReturnLineageModeV1.Lineage), (returns.Stored!.Return.SourceLabelId, returns.Stored.Return.LineageMode));
        Assert.Equal(1, lineage.Calls);
    }

    [Fact]
    public async Task A_lineage_of_several_receipts_of_the_supplier_is_not_staged_yet_and_stays_503()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        var second = Guid.NewGuid();
        returns.Suppliers[request.GoodsAcceptanceId] = Party;
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5"), Root(second, "2", "5")] };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-multi")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (refusal.Status, refusal.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task A_lineage_return_naming_another_receipt_than_the_label_s_sole_receipt_is_a_terminal_409()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        var actual = Guid.NewGuid();
        returns.Suppliers[actual] = Party;
        var lineage = new LineageFake { Roots = [Root(actual, "1", "1")] };

        var answer = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-mismatch")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch), (answer.Status, answer.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task A_quantity_return_does_not_accept_a_manual_cost_because_the_order_price_is_the_supplier_price()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        var other = Guid.NewGuid();
        returns.Suppliers[other] = OtherSupplier;
        var lineage = new LineageFake { Roots = [Root(other, "1", "1")] };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("quantity-manual")));

        Assert.Equal((400, "validation_failed"), (refusal.Status, refusal.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Theory]
    [InlineData("only-supplier-single", "Lineage")]
    [InlineData("other-supplier", "Quantity")]
    [InlineData("production-half", "Quantity")]
    [InlineData("no-receipt", "Quantity")]
    public void SAT_D445_the_planner_distributes_only_a_lineage_of_the_supplier_s_own_receipts(string scenario, string mode)
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var suppliers = new Dictionary<Guid, Guid?> { [mine] = Party, [other] = OtherSupplier };
        PurchaseSupplierReturnLineageRootV1[] roots = scenario switch
        {
            "only-supplier-single" => [Root(mine, "1", "1")],
            "other-supplier" => [Root(other, "1", "1")],
            "production-half" => [Root(mine, "1", "2"), new(false, null, "1", "2")],
            _ => [new(false, null, "1", "1")],
        };

        var plan = PurchaseSupplierReturnLineagePlannerV1.Plan(new(LineageLabel, roots), suppliers, Party, mine);

        Assert.Equal(mode, plan.Mode.ToString());
    }

    [Fact]
    public void The_planner_refuses_shares_that_do_not_add_up_and_an_unknown_supplier_without_guessing()
    {
        var mine = Guid.NewGuid();
        Assert.Equal(503, Assert.Throws<PurchasingServiceException>(() => PurchaseSupplierReturnLineagePlannerV1.Plan(
            new(LineageLabel, [Root(mine, "1", "2")]), new Dictionary<Guid, Guid?> { [mine] = Party }, Party, mine)).Status);
        Assert.Equal(503, Assert.Throws<PurchasingServiceException>(() => PurchaseSupplierReturnLineagePlannerV1.Plan(
            new(LineageLabel, [Root(mine, "1", "1")]), new Dictionary<Guid, Guid?> { [mine] = null }, Party, mine)).Status);
    }

    private static PurchaseSupplierReturnLineageRootV1 Root(Guid acceptance, string numerator, string denominator) =>
        new(true, acceptance, numerator, denominator);

    private sealed class LineageFake : IPurchaseSupplierReturnLineageReadPortV1
    {
        public IReadOnlyList<PurchaseSupplierReturnLineageRootV1> Roots { get; init; } = [];
        public int Calls { get; private set; }

        public Task<PurchaseSupplierReturnLineageV1> ReadAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PurchaseSupplierReturnLineageV1(labelId, Roots));
        }
    }
}
