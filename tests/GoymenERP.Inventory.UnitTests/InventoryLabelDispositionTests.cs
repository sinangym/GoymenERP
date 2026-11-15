using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Scraps;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// F-009 D1 (URT-D31, §4.1): the pure disposition computation shared by the read ports and the output-reversal
/// participant, and the participant's fixed refusal order. What SQL Server stores is proven by the relational
/// acceptance tests.
/// </summary>
public sealed class InventoryLabelDispositionTests
{
    private static readonly Guid Company = Guid.Parse("93000000-0000-4000-8000-000000000001");
    private static readonly Guid Product = Guid.Parse("93000000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("93000000-0000-4000-8000-000000000003");
    private static readonly Guid Order = Guid.Parse("93000000-0000-4000-8000-000000000004");
    private static readonly Guid Output = Guid.Parse("93000000-0000-4000-8000-000000000005");
    private static readonly Guid Plant = Guid.Parse("93000000-0000-4000-8000-000000000006");
    private static readonly Guid Depot = Guid.Parse("93000000-0000-4000-8000-000000000007");

    [Fact]
    public void A_fresh_output_label_is_wholly_reversible_at_its_output_position()
    {
        var snapshot = Compute([Receipt(100m)], [Free(Plant, 100m)]);

        Assert.Equal(InventoryLabelDispositionStatusV1.Value, snapshot.Status);
        var output = Assert.IsType<InventoryLabelOutputSnapshot>(snapshot.Output);
        Assert.Equal((Order, Output, 100m, 0m, 100m, 0m), (output.ManufacturingOrderId, output.OutputId,
            output.ProducedKg, output.ReversedKg, output.FreeUnallocatedAtPositionKg, output.BlockedAtPositionKg));
        Assert.Equal((100m, 0m, 0m, 100m, 1), (snapshot.PhysicalKg, snapshot.BlockedKg, snapshot.AllocatedKg,
            snapshot.FreeUnallocatedKg, snapshot.FreePositionCount));
        Assert.Equal(7L, snapshot.LabelVersion);
    }

    [Fact]
    public void Earlier_reversals_reduce_what_is_left_and_are_counted_against_the_produced_kilograms()
    {
        var snapshot = Compute([Receipt(100m), Reversal(30m), Reversal(20m)], [Free(Plant, 50m)]);

        var output = Assert.IsType<InventoryLabelOutputSnapshot>(snapshot.Output);
        Assert.Equal((100m, 50m, 50m), (output.ProducedKg, output.ReversedKg, output.FreeUnallocatedAtPositionKg));
        Assert.Equal(50m, snapshot.PhysicalKg);
    }

    [Fact]
    public void A_shipment_is_named_and_locks_the_rest_as_shipped()
    {
        var snapshot = Compute([Receipt(100m), Line(StockMovementKind.Issue, StockDirection.Out, 40m,
            InventoryLabelDispositionReader.DefiniteShipmentSourceKind)], [Free(Plant, 60m)]);

        Assert.Equal(40m, snapshot.ShippedKg);
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.Shipped, Locked(snapshot));
    }

    [Fact]
    public void A_customer_return_of_the_whole_shipment_no_longer_locks_as_shipped()
    {
        var snapshot = Compute([
            Receipt(100m),
            Line(StockMovementKind.Issue, StockDirection.Out, 40m, InventoryLabelDispositionReader.DefiniteShipmentSourceKind),
            Line(StockMovementKind.Return, StockDirection.In, 40m, InventoryCustomerReturnContractV1.SourceKind),
        ], [Free(Plant, 100m)]);

        Assert.Equal((40m, 40m), (snapshot.ShippedKg, snapshot.CustomerReturnedKg));
        Assert.Equal(100m, snapshot.Output!.FreeUnallocatedAtPositionKg);
    }

    [Fact]
    public void An_issue_to_manufacturing_locks_as_issued_until_it_is_returned()
    {
        var issued = Compute([Receipt(100m), Line(StockMovementKind.Issue, StockDirection.Out, 25m,
            InventoryManufacturingRealizationContractV1.InputIssueSourceKind)], [Free(Plant, 75m)]);
        Assert.Equal(25m, issued.ManufacturingIssuedKg);
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.IssuedToManufacturing, Locked(issued));

        var returned = Compute([
            Receipt(100m),
            Line(StockMovementKind.Issue, StockDirection.Out, 25m, InventoryManufacturingRealizationContractV1.InputIssueSourceKind),
            Line(StockMovementKind.Return, StockDirection.In, 25m, InventoryManufacturingRealizationContractV1.InputReturnSourceKind),
        ], [Free(Plant, 100m)]);
        Assert.Equal((25m, 25m, 100m), (returned.ManufacturingIssuedKg, returned.ManufacturingReturnedKg,
            returned.Output!.FreeUnallocatedAtPositionKg));
    }

    [Fact]
    public void A_transfer_moves_the_kilograms_away_from_the_output_position()
    {
        var snapshot = Compute([
            Receipt(100m),
            Line(StockMovementKind.TransferDispatch, StockDirection.Out, 100m, InventoryTransfer.SourceKind),
            Line(StockMovementKind.TransferAccept, StockDirection.In, 100m, InventoryTransfer.SourceKind, location: Depot),
        ], [Free(Depot, 100m)]);

        Assert.Equal((100m, 100m), (snapshot.TransferDispatchedKg, snapshot.TransferAcceptedKg));
        Assert.Equal(0m, snapshot.Output!.FreeUnallocatedAtPositionKg);
        // The label is still free elsewhere, which the Free kg port reports; the reversal cannot take it.
        Assert.Equal((100m, 1), (snapshot.FreeUnallocatedKg, snapshot.FreePositionCount));
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.Transferred, Locked(snapshot));
    }

    [Fact]
    public void An_active_allocation_is_not_free_and_locks_as_allocated()
    {
        var snapshot = Compute([Receipt(100m)], [Free(Plant, 100m)], [Free(Plant, 30m)]);

        Assert.Equal((30m, 70m), (snapshot.Output!.AllocatedAtPositionKg, snapshot.Output.FreeUnallocatedAtPositionKg));
        Assert.Equal((30m, 70m), (snapshot.AllocatedKg, snapshot.FreeUnallocatedKg));
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.Allocated, Locked(snapshot));
    }

    [Fact]
    public void Blocked_kilograms_are_never_free_and_lock_as_blocked()
    {
        var snapshot = Compute([
            Receipt(100m),
            Line(StockMovementKind.Block, StockDirection.Out, 10m, "inventory.block"),
            Line(StockMovementKind.Block, StockDirection.In, 10m, "inventory.block", block: BlockState.Blocked),
        ], [Free(Plant, 90m), Balance(Plant, BlockState.Blocked, 10m)]);

        Assert.Equal((100m, 10m), (snapshot.PhysicalKg, snapshot.BlockedKg));
        Assert.Equal((90m, 10m), (snapshot.Output!.FreeUnallocatedAtPositionKg, snapshot.Output.BlockedAtPositionKg));
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.Blocked, Locked(snapshot));
    }

    [Fact]
    public void Any_other_cause_locks_as_not_free()
    {
        var snapshot = Compute([Receipt(100m), Line(StockMovementKind.Scrap, StockDirection.Out, 5m,
            StockScrap.SourceKind)], [Free(Plant, 95m)]);

        Assert.Equal(5m, snapshot.ScrappedKg);
        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.NotFree, Locked(snapshot));
    }

    [Fact]
    public void The_refusal_order_is_shipped_issued_transferred_allocated_blocked()
    {
        var snapshot = Compute([
            Receipt(100m),
            Line(StockMovementKind.Issue, StockDirection.Out, 10m, InventoryLabelDispositionReader.DefiniteShipmentSourceKind),
            Line(StockMovementKind.Issue, StockDirection.Out, 10m, InventoryManufacturingRealizationContractV1.InputIssueSourceKind),
        ], [Free(Plant, 80m)], [Free(Plant, 5m)]);

        Assert.Equal(InventoryManufacturingOutputReversalCodesV1.Shipped, Locked(snapshot));
    }

    [Fact]
    public void A_label_not_received_from_an_output_has_no_output_origin()
    {
        var snapshot = Compute([Line(StockMovementKind.Receipt, StockDirection.In, 100m, "purchasing.goods-receipt")],
            [Free(Plant, 100m)]);

        Assert.Equal(InventoryLabelDispositionStatusV1.Value, snapshot.Status);
        Assert.Null(snapshot.Output);
        Assert.Equal((100m, 100m), (snapshot.OtherInKg, snapshot.FreeUnallocatedKg));
    }

    [Theory]
    [InlineData("balance-does-not-reconcile")]
    [InlineData("reversal-exceeds-receipt")]
    [InlineData("two-output-receipts")]
    [InlineData("reversal-of-another-order")]
    [InlineData("reversal-without-receipt")]
    [InlineData("reversal-as-in-line")]
    [InlineData("negative-balance")]
    [InlineData("duplicate-balance")]
    [InlineData("balance-in-two-places")]
    [InlineData("line-of-another-product")]
    [InlineData("unknown-kind")]
    [InlineData("negative-allocation")]
    public void Stored_figures_that_do_not_reconcile_read_as_unreadable(string fault)
    {
        var snapshot = fault switch
        {
            "balance-does-not-reconcile" => Compute([Receipt(100m)], [Free(Plant, 90m)]),
            "reversal-exceeds-receipt" => Compute([Receipt(100m), Reversal(60m), Reversal(50m)], []),
            "two-output-receipts" => Compute([Receipt(100m), Receipt(10m)], [Free(Plant, 110m)]),
            "reversal-of-another-order" => Compute([Receipt(100m), Reversal(10m) with
            {
                SourceId = Guid.Parse("93000000-0000-4000-8000-000000000099"),
            }], [Free(Plant, 90m)]),
            "reversal-without-receipt" => Compute([
                Line(StockMovementKind.Receipt, StockDirection.In, 100m, "purchasing.goods-receipt"), Reversal(10m),
            ], [Free(Plant, 90m)]),
            "reversal-as-in-line" => Compute([Receipt(100m), Reversal(10m) with { Direction = (int)StockDirection.In }],
                [Free(Plant, 110m)]),
            "negative-balance" => Compute([Receipt(100m)], [Free(Plant, 110m), Balance(Plant, BlockState.Blocked, -10m)]),
            "duplicate-balance" => Compute([Receipt(100m)], [Free(Plant, 50m), Free(Plant, 50m)]),
            "balance-in-two-places" => Compute([Receipt(100m)], [Free(Plant, 50m), Free(Depot, 50m)]),
            "line-of-another-product" => Compute([Receipt(100m) with { ProductId = Order }], [Free(Plant, 100m)]),
            "unknown-kind" => Compute([Receipt(100m), Line((StockMovementKind)99, StockDirection.Out, 1m, "x")],
                [Free(Plant, 99m)]),
            _ => Compute([Receipt(100m)], [Free(Plant, 100m)], [Free(Plant, -1m)]),
        };

        Assert.Equal(InventoryLabelDispositionStatusV1.Unreadable, snapshot.Status);
        Assert.Null(snapshot.Output);
    }

    [Fact]
    public void The_port_contract_formats_kilograms_and_says_whether_any_stock_is_left()
    {
        var inStock = InventoryLabelDispositionReadPort.ToContract(Company,
            Compute([Receipt(100.5m), Reversal(0.25m)], [Free(Plant, 100.25m)]));
        Assert.Equal((InventoryLabelStockStateV1.InStock, "100.25", "7"), (inStock.State, inStock.PhysicalKg,
            inStock.LabelVersion));
        Assert.Equal(("100.5", "0.25", "100.25"), (inStock.Output!.ProducedKg, inStock.Output.ReversedKg,
            inStock.Output.FreeUnallocatedAtOutputPositionKg));

        var reversedAway = InventoryLabelDispositionReadPort.ToContract(Company,
            Compute([Receipt(100m), Reversal(100m)], [Free(Plant, 0m)]));
        Assert.Equal((InventoryLabelStockStateV1.OutOfStock, "0"), (reversedAway.State, reversedAway.PhysicalKg));
    }

    private static string Locked(InventoryLabelDispositionSnapshot snapshot) =>
        InventoryManufacturingRealizationParticipant.LockedReason(snapshot, snapshot.Output!);

    private static InventoryLabelDispositionSnapshot Compute(IReadOnlyList<DispositionLineFact> lines,
        IReadOnlyList<DispositionBalanceFact> balances, IReadOnlyList<DispositionBalanceFact>? allocations = null) =>
        InventoryLabelDispositionReader.Compute(Label, Product, 7L, lines, balances, allocations ?? []);

    private static DispositionLineFact Receipt(decimal kilograms) =>
        Line(StockMovementKind.Receipt, StockDirection.In, kilograms,
            InventoryManufacturingRealizationContractV1.OutputSourceKind, sourceId: Order, sourceLineId: Output);

    private static DispositionLineFact Reversal(decimal kilograms) =>
        Line(StockMovementKind.ManufacturingOutputReversal, StockDirection.Out, kilograms,
            InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind, sourceId: Order,
            sourceLineId: Guid.NewGuid());

    private static DispositionLineFact Line(StockMovementKind kind, StockDirection direction, decimal kilograms,
        string sourceKind, Guid? sourceId = null, Guid? sourceLineId = null, Guid? location = null,
        BlockState block = BlockState.Free) =>
        new(Guid.NewGuid(), (int)kind, sourceKind, sourceId ?? Guid.NewGuid(), sourceLineId, (int)direction,
            (int)StockPartyKind.Company, Company, (int)StockPartyKind.Company, Company, location ?? Plant, Product,
            (int)block, kilograms);

    private static DispositionBalanceFact Free(Guid location, decimal kilograms) =>
        Balance(location, BlockState.Free, kilograms);

    private static DispositionBalanceFact Balance(Guid location, BlockState block, decimal kilograms) =>
        new((int)StockPartyKind.Company, Company, (int)StockPartyKind.Company, Company, location, Product, (int)block,
            kilograms);
}
