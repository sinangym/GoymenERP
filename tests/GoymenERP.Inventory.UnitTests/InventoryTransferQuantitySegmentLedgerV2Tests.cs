using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryTransferQuantitySegmentLedgerV2Tests
{
    private static readonly Guid TransferId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid InitialRevisionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid FirstShareId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondShareId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid FirstLabelId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondLabelId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid InitialSegmentId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public void Twenty_five_dispatched_can_become_fifteen_accepted_and_ten_pending_with_stable_distinct_ids()
    {
        var ledger = SingleShareLedger();
        var acceptedId = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var pendingId = Guid.Parse("60000000-0000-0000-0000-000000000003");

        var result = ledger.Apply(new(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            expectedVersion: 1,
            Partition(SingleShare(25m),
            [
                Segment(acceptedId, InventoryTransferQuantitySegmentStateV2.Accepted, 15m, FirstShareId,
                    parent: InitialSegmentId),
                Segment(pendingId, InventoryTransferQuantitySegmentStateV2.InTransit, 10m, FirstShareId,
                    parent: InitialSegmentId),
            ])));

        Assert.Equal(2, result.Version);
        Assert.Equal(25m, result.Partition.DispatchedQuantity.Value);
        Assert.Equal(15m, result.Partition.AcceptedQuantity.Value);
        Assert.Equal(10m, result.Partition.InTransitQuantity.Value);
        Assert.Equal(0m, result.Partition.DiscrepancyQuantity.Value);
        Assert.Equal([acceptedId, pendingId], result.Partition.Segments.Select(segment => segment.SegmentId));
        Assert.Equal(2, ledger.Revisions.Count);
    }

    [Fact]
    public void Multiple_real_source_shares_are_conserved_independently()
    {
        var sources = new[] { Share(FirstShareId, FirstLabelId, 15m), Share(SecondShareId, SecondLabelId, 10m) };
        var initial = new InventoryTransferQuantityPartitionV2(sources,
        [
            new(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, Kg(25m),
            [
                Allocation(FirstShareId, 15m),
                Allocation(SecondShareId, 10m),
            ]),
        ]);
        var ledger = InventoryTransferQuantitySegmentLedgerV2.Create(
            TransferId, LineId, InitialRevisionId, initial);

        ledger.Apply(new(Guid.NewGuid(), 1, new(sources,
        [
            new(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.Accepted, Kg(15m),
            [
                Allocation(FirstShareId, 10m),
                Allocation(SecondShareId, 5m),
            ], InitialSegmentId),
            new(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, Kg(10m),
            [
                Allocation(FirstShareId, 5m),
                Allocation(SecondShareId, 5m),
            ], InitialSegmentId),
        ])));

        Assert.Equal(25m, ledger.Current.DispatchedQuantity.Value);
        Assert.Equal(15m, ledger.Current.AcceptedQuantity.Value);
        Assert.Equal(10m, ledger.Current.InTransitQuantity.Value);
    }

    [Fact]
    public void Shortage_or_damage_is_a_separate_discrepancy_and_never_accepted_quantity()
    {
        var ledger = SingleShareLedger();

        ledger.Apply(new(Guid.NewGuid(), 1, Partition(SingleShare(25m),
        [
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.Accepted, 14m, FirstShareId,
                InitialSegmentId),
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, 10m, FirstShareId,
                InitialSegmentId),
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.Discrepancy, 1m, FirstShareId,
                InitialSegmentId, InventoryTransferDiscrepancyKindV2.Shortage),
        ])));

        Assert.Equal(14m, ledger.Current.AcceptedQuantity.Value);
        Assert.Equal(10m, ledger.Current.InTransitQuantity.Value);
        Assert.Equal(1m, ledger.Current.DiscrepancyQuantity.Value);
    }

    [Fact]
    public void Stale_expected_version_is_atomic_and_applies_nothing()
    {
        var ledger = SingleShareLedger();
        var before = ledger.Current;

        AssertInvalid(() => ledger.Apply(new(Guid.NewGuid(), 2, Partition(SingleShare(25m),
        [
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.Accepted, 25m, FirstShareId,
                InitialSegmentId),
        ]))));

        Assert.Equal(1, ledger.Version);
        Assert.Single(ledger.Revisions);
        Assert.Same(before, ledger.Current);
    }

    [Fact]
    public void A_terminal_segment_cannot_be_silently_rewritten_or_removed()
    {
        var ledger = AcceptedFifteenPendingTen();
        var accepted = ledger.Current.Segments.Single(segment =>
            segment.State == InventoryTransferQuantitySegmentStateV2.Accepted);
        var pending = ledger.Current.Segments.Single(segment =>
            segment.State == InventoryTransferQuantitySegmentStateV2.InTransit);

        AssertInvalid(() => ledger.Apply(new(Guid.NewGuid(), 2, Partition(SingleShare(25m),
        [
            Segment(accepted.SegmentId, InventoryTransferQuantitySegmentStateV2.Accepted, 14m, FirstShareId,
                InitialSegmentId),
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, 11m, FirstShareId,
                pending.SegmentId),
        ]))));

        Assert.Equal(2, ledger.Version);
        Assert.Equal(15m, ledger.Current.AcceptedQuantity.Value);
    }

    [Fact]
    public void Reusing_a_segment_id_with_different_facts_is_refused_even_by_a_reversal()
    {
        var ledger = AcceptedFifteenPendingTen();
        var accepted = ledger.Current.Segments.Single(segment =>
            segment.State == InventoryTransferQuantitySegmentStateV2.Accepted);

        AssertInvalid(() => ledger.Apply(new(
            Guid.NewGuid(),
            2,
            Partition(SingleShare(25m),
            [
                Segment(accepted.SegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, 25m, FirstShareId),
            ]),
            ledger.Revisions[^1].RevisionId,
            "Yanlış kabulü düzelt")));
    }

    [Fact]
    public void Linked_reversal_can_restore_an_earlier_partition_without_erasing_history()
    {
        var ledger = AcceptedFifteenPendingTen();
        var acceptedRevision = ledger.Revisions[^1];
        var initialPartition = ledger.Revisions[0].Partition;

        var reversal = ledger.Apply(new(
            Guid.NewGuid(),
            expectedVersion: 2,
            initialPartition,
            reversesRevisionId: acceptedRevision.RevisionId,
            reason: "Hedef kabul belgesi ters kaydı"));

        Assert.Equal(3, ledger.Version);
        Assert.Equal(3, ledger.Revisions.Count);
        Assert.Equal(acceptedRevision.RevisionId, reversal.ReversesRevisionId);
        Assert.Same(initialPartition, reversal.Partition);
        Assert.Equal(25m, ledger.Current.InTransitQuantity.Value);
        Assert.Equal(0m, ledger.Current.AcceptedQuantity.Value);
        Assert.Equal(15m, acceptedRevision.Partition.AcceptedQuantity.Value);
    }

    [Fact]
    public void Reversal_requires_a_recorded_target_and_a_reason()
    {
        var ledger = AcceptedFifteenPendingTen();

        AssertInvalid(() => ledger.Apply(new(
            Guid.NewGuid(), 2, ledger.Revisions[0].Partition, Guid.NewGuid(), "Düzeltme")));
        AssertInvalid(() => _ = new InventoryTransferQuantityRevisionV2(
            Guid.NewGuid(), 2, ledger.Revisions[0].Partition, ledger.Revisions[^1].RevisionId, "  "));
    }

    [Fact]
    public void Forward_children_must_conserve_each_removed_pending_source_share()
    {
        var sources = new[] { Share(FirstShareId, FirstLabelId, 15m), Share(SecondShareId, SecondLabelId, 10m) };
        var initial = new InventoryTransferQuantityPartitionV2(sources,
        [
            new(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, Kg(25m),
            [
                Allocation(FirstShareId, 15m),
                Allocation(SecondShareId, 10m),
            ]),
        ]);
        var ledger = InventoryTransferQuantitySegmentLedgerV2.Create(
            TransferId, LineId, InitialRevisionId, initial);

        AssertInvalid(() => ledger.Apply(new(Guid.NewGuid(), 1, new(sources,
        [
            new(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.Accepted, Kg(15m),
            [
                Allocation(FirstShareId, 15m),
            ], InitialSegmentId),
            new(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, Kg(10m),
            [
                Allocation(SecondShareId, 10m),
            ], Guid.NewGuid()),
        ]))));
    }

    [Fact]
    public void A_partition_rejects_unknown_or_unbalanced_source_allocations()
    {
        AssertInvalid(() => Partition(SingleShare(25m),
        [
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, 24m, FirstShareId),
        ]));

        AssertInvalid(() => Partition(SingleShare(25m),
        [
            Segment(Guid.NewGuid(), InventoryTransferQuantitySegmentStateV2.InTransit, 25m, Guid.NewGuid()),
        ]));
    }

    [Fact]
    public void Source_and_segment_identities_are_required_and_unique()
    {
        AssertInvalid(() => _ = new InventoryTransferSourceShareV2(Guid.Empty, FirstLabelId, 1, Kg(1m)));
        AssertInvalid(() => _ = new InventoryTransferSourceShareV2(FirstShareId, Guid.Empty, 1, Kg(1m)));
        AssertInvalid(() => _ = new InventoryTransferSourceShareV2(FirstShareId, FirstLabelId, 0, Kg(1m)));
        AssertInvalid(() => _ = new InventoryTransferSegmentSourceAllocationV2(Guid.Empty, Kg(1m)));
        AssertInvalid(() => _ = new InventoryTransferQuantitySegmentV2(
            Guid.Empty, InventoryTransferQuantitySegmentStateV2.InTransit, Kg(1m),
            [Allocation(FirstShareId, 1m)]));

        var source = SingleShare(25m);
        AssertInvalid(() => _ = new InventoryTransferQuantityPartitionV2([source, source],
            [Segment(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, 50m, FirstShareId)]));
        var segment = Segment(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, 25m,
            FirstShareId);
        AssertInvalid(() => _ = new InventoryTransferQuantityPartitionV2([source], [segment, segment]));
    }

    [Fact]
    public void Initial_partition_is_only_unparented_in_transit_quantity()
    {
        var accepted = Segment(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.Accepted, 25m,
            FirstShareId);
        AssertInvalid(() => InventoryTransferQuantitySegmentLedgerV2.Create(
            TransferId, LineId, InitialRevisionId, Partition(SingleShare(25m), [accepted])));

        var parented = Segment(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, 25m,
            FirstShareId, Guid.NewGuid());
        AssertInvalid(() => InventoryTransferQuantitySegmentLedgerV2.Create(
            TransferId, LineId, InitialRevisionId, Partition(SingleShare(25m), [parented])));
    }

    [Fact]
    public void Segment_contract_does_not_claim_a_physical_identity_or_location()
    {
        var names = typeof(InventoryTransferQuantitySegmentV2).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("LabelId", names);
        Assert.DoesNotContain("LocationId", names);
        Assert.DoesNotContain("CustodianId", names);
        Assert.DoesNotContain("OwnerId", names);
    }

    [Fact]
    public void Whole_label_v1_acceptance_contract_remains_quantity_free()
    {
        Assert.DoesNotContain(typeof(AcceptInventoryTransferLineV1).GetProperties(),
            property => property.Name.Contains("Quantity", StringComparison.Ordinal)
                        || property.Name.Contains("Segment", StringComparison.Ordinal));
    }

    private static InventoryTransferQuantitySegmentLedgerV2 AcceptedFifteenPendingTen()
    {
        var ledger = SingleShareLedger();
        ledger.Apply(new(Guid.Parse("30000000-0000-0000-0000-000000000002"), 1,
            Partition(SingleShare(25m),
            [
                Segment(Guid.Parse("60000000-0000-0000-0000-000000000002"),
                    InventoryTransferQuantitySegmentStateV2.Accepted, 15m, FirstShareId, InitialSegmentId),
                Segment(Guid.Parse("60000000-0000-0000-0000-000000000003"),
                    InventoryTransferQuantitySegmentStateV2.InTransit, 10m, FirstShareId, InitialSegmentId),
            ])));
        return ledger;
    }

    private static InventoryTransferQuantitySegmentLedgerV2 SingleShareLedger()
    {
        var source = SingleShare(25m);
        return InventoryTransferQuantitySegmentLedgerV2.Create(
            TransferId,
            LineId,
            InitialRevisionId,
            Partition(source,
            [
                Segment(InitialSegmentId, InventoryTransferQuantitySegmentStateV2.InTransit, 25m, FirstShareId),
            ]));
    }

    private static InventoryTransferSourceShareV2 SingleShare(decimal quantity) =>
        Share(FirstShareId, FirstLabelId, quantity);

    private static InventoryTransferSourceShareV2 Share(Guid shareId, Guid labelId, decimal quantity) =>
        new(shareId, labelId, 7, Kg(quantity));

    private static InventoryTransferSegmentSourceAllocationV2 Allocation(Guid sourceShareId, decimal quantity) =>
        new(sourceShareId, Kg(quantity));

    private static InventoryTransferQuantitySegmentV2 Segment(
        Guid segmentId,
        InventoryTransferQuantitySegmentStateV2 state,
        decimal quantity,
        Guid sourceShareId,
        Guid? parent = null,
        InventoryTransferDiscrepancyKindV2? discrepancy = null) =>
        new(segmentId, state, Kg(quantity), [Allocation(sourceShareId, quantity)], parent, discrepancy);

    private static InventoryTransferQuantityPartitionV2 Partition(
        InventoryTransferSourceShareV2 source,
        IEnumerable<InventoryTransferQuantitySegmentV2> segments) =>
        new([source], segments);

    private static Kilograms Kg(decimal value) => Kilograms.Of(value);

    private static void AssertInvalid(Action action)
    {
        var error = Assert.Throws<InventoryRuleException>(action);
        Assert.Equal(InventoryRuleCodes.InvalidTransfer, error.Code);
    }
}
