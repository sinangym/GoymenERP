using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>Pure owner-fact reconciliation only; these tests are not real-SQL acceptance evidence.</summary>
public sealed class InventoryCostLineageReaderModelTests
{
    private static readonly Guid Company = Id(1);
    private static readonly Guid Product = Id(2);
    private static readonly Guid Root = Id(3);
    private static readonly Guid Other = Id(4);
    private static readonly Guid Third = Id(5);
    private static readonly Guid Left = Id(6);
    private static readonly Guid Right = Id(7);
    private static readonly Guid Merged = Id(8);
    private static readonly Guid Location = Id(9);
    private const string ReceiptSource = "synthetic.receipt";

    [Fact]
    public void Real_receipt_shipment_and_bound_customer_return_reconcile_separate_exact_totals()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        var shipment = world.Shipment(Root, 30m);
        world.CustomerReturn(Root, 10m, shipment);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "80", "1");
        Ratio(snapshot.ShippedKg, "30", "1");
        Ratio(snapshot.CustomerReturnedKg, "10", "1");
        Ratio(snapshot.NetShippedKg, "20", "1");
        var root = Assert.Single(snapshot.Labels);
        Assert.Equal((Root, 7L, "1"), (root.LabelId, root.Version, root.ProductVersion));
        Assert.Equal(80m, Assert.Single(root.Positions).QuantityKg);
        var captured = Assert.Single(snapshot.Shipments);
        var source = world.Movements.Single(row => row.MovementId == shipment);
        Assert.Equal((source.SourceId, shipment, 1, source.SourceLineId!.Value, Root, Product),
            (captured.ShipmentId, captured.MovementId, captured.LineNumber, captured.LoadingInstructionLineId, captured.LabelId, captured.ProductId));
        Ratio(captured.RootShare, "1", "1");
        Ratio(captured.NetRootQuantityKg, "20", "1");
        var returned = Assert.Single(captured.Returns);
        Assert.Equal(shipment, returned.SourceIssueMovementId);
        Assert.Equal(10m, returned.QuantityKg);
        Assert.Equal(world.Movements.Single(row => row.MovementId == returned.MovementId).SourceLineId, returned.ReturnId);
    }

    [Fact]
    public void Split_balances_reconcile_with_repack_mass_without_fabricated_movement_receipts()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Repack(InventoryLabelTransformationKind.Split, [(Root, 100m)], [(Left, 40m), (Right, 60m)]);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "100", "1");
        Ratio(snapshot.NetShippedKg, "0", "1");
        Assert.Equal(3, snapshot.Labels.Count);
        Assert.All(snapshot.Labels, label => Ratio(label.RootShare, "1", "1"));
        Assert.Equal(0m, Assert.Single(snapshot.Labels.Single(label => label.LabelId == Root).Positions).QuantityKg);
        Assert.Single(world.Lines);
    }

    [Fact]
    public void A_merged_140_kilogram_balance_contains_70_root_kilograms_and_zero_share_siblings_are_retained()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Receipt(Other, 100m);
        world.Repack(InventoryLabelTransformationKind.Merge, [(Root, 100m), (Other, 100m)], [(Merged, 200m)]);
        world.Shipment(Merged, 60m);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "70", "1");
        Ratio(snapshot.ShippedKg, "30", "1");
        Ratio(snapshot.Labels.Single(label => label.LabelId == Merged).RootShare, "1", "2");
        Ratio(snapshot.Labels.Single(label => label.LabelId == Other).RootShare, "0", "1");
        Assert.Equal(140m, Assert.Single(snapshot.Labels.Single(label => label.LabelId == Merged).Positions).QuantityKg);
    }

    [Fact]
    public void A_three_way_merge_preserves_nonterminating_root_weighted_remaining_and_shipped_kilograms()
    {
        var world = new World();
        world.Receipt(Root, 1m);
        world.Receipt(Other, 1m);
        world.Receipt(Third, 1m);
        world.Repack(InventoryLabelTransformationKind.Merge, [(Root, 1m), (Other, 1m), (Third, 1m)], [(Merged, 3m)]);
        world.Shipment(Merged, 1m);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "2", "3");
        Ratio(snapshot.ShippedKg, "1", "3");
        Assert.False(snapshot.RemainingKg!.TryToDecimal(out _));
        var shipment = Assert.Single(snapshot.Shipments);
        Ratio(shipment.RootShare, "1", "3");
        Ratio(shipment.NetRootQuantityKg, "1", "3");
        Assert.Empty(shipment.Returns);
    }

    [Fact]
    public void A_fully_returned_shipment_keeps_both_real_movement_identities_in_the_frozen_snapshot()
    {
        var world = new World(); world.Receipt(Root, 10m);
        var issue = world.Shipment(Root, 5m); var returned = world.CustomerReturn(Root, 5m, issue);
        var snapshot = Value(world);
        var shipment = Assert.Single(snapshot.Shipments);
        Assert.Equal(issue, shipment.MovementId);
        Ratio(shipment.NetRootQuantityKg, "0", "1");
        Assert.Equal(returned, Assert.Single(shipment.Returns).MovementId);
        world.CustomerReturn(Root, 1m, issue); // Later invalid source changes cannot mutate the earlier snapshot.
        Assert.Single(shipment.Returns);
        Ratio(shipment.NetRootQuantityKg, "0", "1");
    }

    [Fact]
    public void A_physical_exit_before_a_later_split_preserves_the_reduced_whole_label_fraction()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Receipt(Other, 100m);
        world.Repack(InventoryLabelTransformationKind.Merge, [(Root, 100m), (Other, 100m)], [(Merged, 200m)]);
        world.Shipment(Merged, 60m);
        world.Repack(InventoryLabelTransformationKind.Split, [(Merged, 140m)], [(Left, 80m), (Right, 60m)]);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "70", "1");
        Ratio(snapshot.NetShippedKg, "30", "1");
        Ratio(snapshot.Labels.Single(label => label.LabelId == Left).RootShare, "1", "2");
        Ratio(snapshot.Labels.Single(label => label.LabelId == Right).RootShare, "1", "2");
    }

    [Fact]
    public void A_split_merge_diamond_does_not_count_ancestor_and_child_balances_twice()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Repack(InventoryLabelTransformationKind.Split, [(Root, 100m)], [(Left, 40m), (Right, 60m)]);
        world.Repack(InventoryLabelTransformationKind.Merge, [(Left, 40m), (Right, 60m)], [(Merged, 100m)]);
        world.Shipment(Merged, 20m);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "80", "1");
        Ratio(snapshot.ShippedKg, "20", "1");
        Ratio(snapshot.Labels.Single(label => label.LabelId == Merged).RootShare, "1", "1");
    }

    [Fact]
    public void Manufacturing_genealogy_does_not_add_its_output_mass_to_the_real_receipt_a_second_time()
    {
        var world = new World();
        world.Receipt(Root, 100m, InventoryManufacturingRealizationContractV1.OutputSourceKind);
        var production = Id(50);
        world.Headers.Add(new(Company, production, (int)InventoryLabelTransformationKind.Manufacturing));
        // A production's raw input can belong to another product and is outside this repack component.
        world.Inputs.Add(new(Company, production, 1, Other, 1, 120m));
        world.Outputs.Add(new(Company, production, 1, Root, 1, 100m));

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "100", "1");
        Assert.Equal(Root, Assert.Single(snapshot.Labels).LabelId);
    }

    [Theory]
    [InlineData("inventory.transfer")]
    [InlineData("manufacturing.input-issue")]
    [InlineData("LOGISTICS.DEFINITE-SHIPMENT")]
    public void Missing_physical_kilograms_are_not_inferred_to_be_a_definite_shipment(string sourceKind)
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Move(Root, sourceKind == "inventory.transfer" ? StockMovementKind.TransferDispatch : StockMovementKind.Issue,
            StockDirection.Out, 100m, sourceKind);

        var snapshot = Value(world);

        Ratio(snapshot.RemainingKg, "0", "1");
        Ratio(snapshot.ShippedKg, "0", "1");
        Ratio(snapshot.NetShippedKg, "0", "1");
    }

    [Fact]
    public void An_existing_empty_label_is_a_readable_zero_snapshot_and_a_missing_label_is_not_found()
    {
        var world = new World();
        var existing = Value(world);
        Ratio(existing.RemainingKg, "0", "1");
        Assert.Empty(Assert.Single(existing.Labels).Positions);
        world.Labels.Clear();

        var missing = world.Read();

        Assert.Equal(InventoryCostLineageOutcome.NotFound, missing.Outcome);
        Assert.Null(missing.RemainingKg);
        Assert.Null(missing.NetShippedKg);
        Assert.Empty(missing.Labels);
    }

    [Theory]
    [InlineData("label-company")]
    [InlineData("label-product")]
    [InlineData("label-version")]
    [InlineData("label-product-version")]
    [InlineData("movement-company")]
    [InlineData("movement-kind")]
    [InlineData("movement-grain")]
    [InlineData("missing-movement")]
    [InlineData("line-company")]
    [InlineData("line-product")]
    [InlineData("line-direction")]
    [InlineData("line-quantity")]
    [InlineData("balance-company")]
    [InlineData("balance-product")]
    [InlineData("balance-negative")]
    [InlineData("balance-duplicate")]
    [InlineData("balance-mismatch")]
    public void Foreign_malformed_and_unreconciled_owner_facts_are_unreadable_without_empty_success(string fault)
    {
        var world = new World();
        world.Receipt(Root, 100m);
        switch (fault)
        {
            case "label-company": world.Labels[0] = world.Labels[0] with { CompanyId = Id(99) }; break;
            case "label-product": world.Labels[0] = world.Labels[0] with { ProductId = Id(99) }; break;
            case "label-version": world.Labels[0] = world.Labels[0] with { Version = 0 }; break;
            case "label-product-version": world.Labels[0] = world.Labels[0] with { ProductVersion = " " }; break;
            case "movement-company": world.Movements[0] = world.Movements[0] with { CompanyId = Id(99) }; break;
            case "movement-kind": world.Movements[0] = world.Movements[0] with { Kind = 999 }; break;
            case "movement-grain": world.Movements[0] = world.Movements[0] with { SourceGrain = 999 }; break;
            case "missing-movement": world.Movements.Clear(); break;
            case "line-company": world.Lines[0] = world.Lines[0] with { CompanyId = Id(99) }; break;
            case "line-product": world.Lines[0] = world.Lines[0] with { ProductId = Id(99) }; break;
            case "line-direction": world.Lines[0] = world.Lines[0] with { Direction = (int)StockDirection.Out }; break;
            case "line-quantity": world.Lines[0] = world.Lines[0] with { QuantityKg = 0m }; break;
            case "balance-company": world.Balances[0] = world.Balances[0] with { CompanyId = Id(99) }; break;
            case "balance-product": world.Balances[0] = world.Balances[0] with { ProductId = Id(99) }; break;
            case "balance-negative": world.Balances[0] = world.Balances[0] with { QuantityKg = -1m }; break;
            case "balance-duplicate": world.Balances.Add(world.Balances[0]); break;
            case "balance-mismatch": world.Balances[0] = world.Balances[0] with { QuantityKg = 99m }; break;
        }

        Unreadable(world);
    }

    [Theory]
    [InlineData("edge-company")]
    [InlineData("edge-version")]
    [InlineData("edge-sequence")]
    [InlineData("edge-quantity")]
    [InlineData("edge-missing-label")]
    [InlineData("missing-header")]
    [InlineData("header-company")]
    [InlineData("header-kind")]
    [InlineData("unrelated-label")]
    public void Incomplete_foreign_or_malformed_repack_components_are_refused(string fault)
    {
        var world = new World();
        world.Receipt(Root, 100m);
        world.Repack(InventoryLabelTransformationKind.Split, [(Root, 100m)], [(Left, 40m), (Right, 60m)]);
        switch (fault)
        {
            case "edge-company": world.Inputs[0] = world.Inputs[0] with { CompanyId = Id(99) }; break;
            case "edge-version": world.Inputs[0] = world.Inputs[0] with { LabelVersion = 7 }; break;
            case "edge-sequence": world.Outputs[1] = world.Outputs[1] with { Sequence = 3 }; break;
            case "edge-quantity": world.Outputs[1] = world.Outputs[1] with { QuantityKg = 61m }; break;
            case "edge-missing-label": world.Labels.RemoveAll(row => row.LabelId == Right); break;
            case "missing-header": world.Headers.Clear(); break;
            case "header-company": world.Headers[0] = world.Headers[0] with { CompanyId = Id(99) }; break;
            case "header-kind": world.Headers[0] = world.Headers[0] with { Kind = 999 }; break;
            case "unrelated-label": world.Labels.Add(new(Company, Other, Product, "1", 7)); break;
        }

        Unreadable(world);
    }

    [Fact]
    public void A_mass_preserving_cycle_cannot_be_accepted_as_an_empty_physical_component()
    {
        var world = new World();
        world.Repack(InventoryLabelTransformationKind.Split, [(Root, 2m)], [(Left, 1m), (Right, 1m)]);
        world.Repack(InventoryLabelTransformationKind.Merge, [(Left, 1m), (Right, 1m)], [(Root, 2m)]);

        Unreadable(world);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("excess")]
    public void A_customer_return_requires_its_actual_same_label_shipment_and_remaining_return_capacity(string fault)
    {
        var world = new World();
        world.Receipt(Root, 100m);
        var shipment = world.Shipment(Root, 10m);
        world.CustomerReturn(Root, fault == "excess" ? 11m : 5m, fault == "unbound" ? Id(99) : shipment);

        Unreadable(world);
    }

    [Fact]
    public void Label_and_position_snapshots_are_immutable_copies_of_the_owner_facts()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        var snapshot = Value(world);
        world.Labels[0] = world.Labels[0] with { Version = 8 };
        world.Balances[0] = world.Balances[0] with { QuantityKg = 1m };

        var root = Assert.Single(snapshot.Labels);
        Assert.Equal(7L, root.Version);
        Assert.Equal(100m, Assert.Single(root.Positions).QuantityKg);
        Assert.Throws<NotSupportedException>(() => ((IList<InventoryCostLineageLabelSnapshot>)snapshot.Labels).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<InventoryCostLineagePositionSnapshot>)root.Positions).Clear());
    }

    [Fact]
    public void Caller_cancellation_is_propagated_instead_of_becoming_an_unreadable_or_empty_snapshot()
    {
        var world = new World();
        world.Receipt(Root, 100m);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.ThrowsAny<OperationCanceledException>(() => world.Read(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static InventoryCostLineageSnapshot Value(World world)
    {
        var snapshot = world.Read();
        Assert.Equal(InventoryCostLineageOutcome.Value, snapshot.Outcome);
        Assert.Null(snapshot.Reason);
        return snapshot;
    }

    private static void Unreadable(World world)
    {
        var snapshot = world.Read();
        Assert.Equal(InventoryCostLineageOutcome.Unreadable, snapshot.Outcome);
        Assert.Equal(InventoryCostLineageReader.UnreadableReason, snapshot.Reason);
        Assert.Empty(snapshot.Labels);
        Assert.Empty(snapshot.Shipments);
        Assert.Null(snapshot.RemainingKg);
        Assert.Null(snapshot.ShippedKg);
        Assert.Null(snapshot.CustomerReturnedKg);
        Assert.Null(snapshot.NetShippedKg);
    }

    private static void Ratio(ExactCostRatio? value, string numerator, string denominator)
    {
        Assert.NotNull(value);
        Assert.Equal((numerator, denominator), (value.Numerator, value.Denominator));
    }

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private sealed class World
    {
        private int sequence = 1000;
        public List<InventoryCostLineageLabelFact> Labels { get; } = [new(Company, Root, Product, "1", 7)];
        public List<InventoryCostLineageTransformationFact> Headers { get; } = [];
        public List<InventoryCostLineageEdgeFact> Inputs { get; } = [];
        public List<InventoryCostLineageEdgeFact> Outputs { get; } = [];
        public List<InventoryCostLineageMovementFact> Movements { get; } = [];
        public List<InventoryCostLineageMovementLineFact> Lines { get; } = [];
        public List<InventoryCostLineageBalanceFact> Balances { get; } = [];

        public InventoryCostLineageSnapshot Read(CancellationToken token = default) => InventoryCostLineageReader.Compute(
            Company, Product, Root, Labels, Headers, Inputs, Outputs, Movements, Lines, Balances, token);

        public Guid Receipt(Guid label, decimal kg, string source = ReceiptSource) => Move(label, StockMovementKind.Receipt, StockDirection.In, kg, source);
        public Guid Shipment(Guid label, decimal kg) => Move(label, StockMovementKind.Issue, StockDirection.Out, kg,
            InventoryLabelDispositionReader.DefiniteShipmentSourceKind);
        public Guid CustomerReturn(Guid label, decimal kg, Guid issue) => Move(label, StockMovementKind.Return, StockDirection.In, kg,
            InventoryCustomerReturnContractV1.SourceKind, issue);

        public Guid Move(Guid label, StockMovementKind kind, StockDirection direction, decimal kg, string sourceKind, Guid? sourceId = null)
        {
            EnsureLabel(label);
            var id = Next();
            Movements.Add(new(Company, id, (int)kind, sourceKind, sourceId ?? Next(), Next(), (int)StockSourceGrain.Line,
                new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero).AddTicks(sequence)));
            Lines.Add(new(Company, id, 1, label, Product, (int)direction, (int)StockPartyKind.Company, Company,
                (int)StockPartyKind.Company, Company, Location, (int)BlockState.Free, kg));
            ChangeBalance(label, direction == StockDirection.In ? kg : -kg);
            return id;
        }

        public void Repack(InventoryLabelTransformationKind kind, (Guid Label, decimal Kg)[] from, (Guid Label, decimal Kg)[] to)
        {
            var id = Next();
            Headers.Add(new(Company, id, (int)kind));
            foreach (var (source, ordinal) in from.Select((value, position) => (value, position)))
            {
                EnsureLabel(source.Label);
                Inputs.Add(new(Company, id, ordinal + 1, source.Label, 6, source.Kg));
                ChangeBalance(source.Label, -source.Kg);
            }
            foreach (var (output, ordinal) in to.Select((value, position) => (value, position)))
            {
                EnsureLabel(output.Label);
                Outputs.Add(new(Company, id, ordinal + 1, output.Label, 1, output.Kg));
                ChangeBalance(output.Label, output.Kg);
            }
        }

        private void EnsureLabel(Guid label)
        {
            if (!Labels.Any(row => row.LabelId == label)) Labels.Add(new(Company, label, Product, "1", 7));
        }

        private void ChangeBalance(Guid label, decimal delta)
        {
            var index = Balances.FindIndex(row => row.LabelId == label);
            if (index < 0) Balances.Add(new(Company, label, Product, (int)StockPartyKind.Company, Company,
                (int)StockPartyKind.Company, Company, Location, (int)BlockState.Free, delta));
            else Balances[index] = Balances[index] with { QuantityKg = Balances[index].QuantityKg + delta };
        }

        private Guid Next() => Id(sequence++);
    }
}
