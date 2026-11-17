using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryCostLineageSharesTests
{
    private static readonly Guid Root = Id(1);
    private static readonly Guid Other = Id(2);
    private static readonly Guid Third = Id(3);
    private static readonly Guid Left = Id(4);
    private static readonly Guid Right = Id(5);
    private static readonly Guid Merged = Id(6);

    [Fact]
    public void A_root_without_transformations_keeps_its_whole_material_share()
    {
        var shares = Resolve([]);

        Assert.Equal(Root, Assert.Single(shares).Key);
        Ratio(shares[Root], "1", "1");
    }

    [Fact]
    public void A_mixed_label_with_140_remaining_kilograms_contains_70_root_kilograms()
    {
        var shares = Resolve([Merge(10, [Mass(Root, 100m), Mass(Other, 100m)], Mass(Merged, 200m))]);

        Ratio(shares[Merged], "1", "2");
        Ratio(shares[Merged].Multiply(140m), "70", "1");
        Assert.Equal(2, shares.Count);
        Assert.False(shares.ContainsKey(Other));
    }

    [Fact]
    public void An_equal_three_source_merge_keeps_an_exact_third_without_decimal_rounding()
    {
        var shares = Resolve([Merge(10, [Mass(Root, 1m), Mass(Other, 1m), Mass(Third, 1m)], Mass(Merged, 3m))]);

        Ratio(shares[Merged], "1", "3");
        Assert.False(shares[Merged].TryToDecimal(out _));
        Ratio(shares[Merged].Multiply(3m), "1", "1");
    }

    [Fact]
    public void Fractional_kilogram_inputs_keep_their_exact_material_share()
    {
        var shares = Resolve([Merge(10, [Mass(Root, 0.1m), Mass(Other, 0.2m)], Mass(Merged, 0.3m))]);

        Ratio(shares[Merged], "1", "3");
        Ratio(shares[Merged].Multiply(0.3m), "1", "10");
    }

    [Fact]
    public void Every_split_child_keeps_the_parent_material_fraction()
    {
        var shares = Resolve([
            Merge(10, [Mass(Root, 100m), Mass(Other, 100m)], Mass(Merged, 200m)),
            Split(11, Mass(Merged, 200m), [Mass(Left, 140m), Mass(Right, 60m)]),
        ]);

        Ratio(shares[Left], "1", "2");
        Ratio(shares[Right], "1", "2");
        Ratio(shares[Left].Multiply(140m), "70", "1");
        Ratio(shares[Right].Multiply(60m), "30", "1");
    }

    [Fact]
    public void A_physical_exit_before_a_whole_remaining_label_split_preserves_the_material_fraction()
    {
        var shares = Resolve([
            Split(11, Mass(Merged, 140m), [Mass(Left, 80m), Mass(Right, 60m)]),
            Merge(10, [Mass(Root, 100m), Mass(Other, 100m)], Mass(Merged, 200m)),
        ]);

        Ratio(shares[Merged], "1", "2");
        Ratio(shares[Left], "1", "2");
        Ratio(shares[Right], "1", "2");
        Ratio(shares[Left].Multiply(80m).Add(shares[Right].Multiply(60m)), "70", "1");
    }

    [Fact]
    public void A_split_and_merge_diamond_counts_each_root_kilogram_once()
    {
        var shares = Resolve([
            Split(10, Mass(Root, 100m), [Mass(Left, 40m), Mass(Right, 60m)]),
            Merge(11, [Mass(Left, 40m), Mass(Right, 60m)], Mass(Merged, 100m)),
        ]);

        Ratio(shares[Left], "1", "1");
        Ratio(shares[Right], "1", "1");
        Ratio(shares[Merged], "1", "1");
        Ratio(shares[Merged].Multiply(100m), "100", "1");
    }

    [Fact]
    public void Reconverging_branches_use_their_own_mass_and_do_not_add_coefficients()
    {
        var mixed = Id(7);
        var shares = Resolve([
            Split(10, Mass(Root, 100m), [Mass(Left, 30m), Mass(Right, 70m)]),
            Merge(11, [Mass(Left, 30m), Mass(Other, 30m)], Mass(mixed, 60m)),
            Merge(12, [Mass(mixed, 60m), Mass(Right, 70m)], Mass(Merged, 130m)),
        ]);

        Ratio(shares[mixed], "1", "2");
        Ratio(shares[Merged], "10", "13");
        Ratio(shares[Merged].Multiply(130m), "100", "1");
    }

    [Fact]
    public void Source_and_transformation_order_do_not_change_the_result()
    {
        var split = Split(10, Mass(Root, 3m), [Mass(Left, 1m), Mass(Right, 2m)]);
        var merge = Merge(11, [Mass(Right, 2m), Mass(Other, 3m), Mass(Left, 1m)], Mass(Merged, 6m));

        var shares = Resolve([merge, split]);

        Ratio(shares[Merged], "1", "2");
        Ratio(shares[Left], "1", "1");
        Ratio(shares[Right], "1", "1");
    }

    [Fact]
    public void Decimal_mass_totals_are_compared_exactly_without_overflow_or_tolerance()
    {
        Refused([Merge(10,
            [Mass(Root, decimal.MaxValue), Mass(Other, decimal.MaxValue)],
            Mass(Merged, decimal.MaxValue))], InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void A_root_may_be_an_existing_output_without_inheriting_its_own_ancestors()
    {
        var shares = Resolve([
            Merge(10, [Mass(Other, 40m), Mass(Third, 60m)], Mass(Root, 100m)),
            Split(11, Mass(Root, 100m), [Mass(Left, 25m), Mass(Right, 75m)]),
        ]);

        Ratio(shares[Root], "1", "1");
        Ratio(shares[Left], "1", "1");
        Assert.False(shares.ContainsKey(Other));
        Assert.False(shares.ContainsKey(Third));
    }

    [Fact]
    public void Manufacturing_does_not_copy_material_shares_into_its_output_or_later_splits()
    {
        var shares = Resolve([
            new InventoryCostLineageTransformation(Id(10), InventoryLabelTransformationKind.Manufacturing,
                [Mass(Root, 100m)], [Mass(Merged, 80m)]),
            Split(11, Mass(Merged, 80m), [Mass(Left, 40m), Mass(Right, 40m)]),
        ]);

        Assert.Equal(Root, Assert.Single(shares).Key);
        Ratio(shares[Root], "1", "1");
    }

    [Fact]
    public void A_whole_label_cannot_be_transformed_twice()
    {
        Refused([
            Split(10, Mass(Root, 100m), [Mass(Left, 40m), Mass(Right, 60m)]),
            Split(11, Mass(Root, 100m), [Mass(Merged, 40m), Mass(Other, 60m)]),
        ], InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void An_output_cannot_have_two_producers()
    {
        Refused([
            Split(10, Mass(Root, 100m), [Mass(Left, 40m), Mass(Right, 60m)]),
            Split(11, Mass(Other, 100m), [Mass(Left, 40m), Mass(Merged, 60m)]),
        ], InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void A_produced_label_cannot_gain_mass_before_its_next_transformation()
    {
        Refused([
            Split(10, Mass(Root, 100m), [Mass(Left, 60m), Mass(Right, 40m)]),
            Merge(11, [Mass(Left, 70m), Mass(Other, 50m)], Mass(Merged, 120m)),
        ], InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void A_longer_cycle_is_refused_even_when_every_event_preserves_mass()
    {
        Refused([
            Split(10, Mass(Root, 2m), [Mass(Left, 1m), Mass(Right, 1m)]),
            Merge(11, [Mass(Left, 1m), Mass(Right, 1m)], Mass(Root, 2m)),
        ], InventoryCostLineageShares.CycleDetected);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void The_longest_path_is_bounded_at_64_transformations(int depth, bool allowed)
    {
        var accepted = InventoryCostLineageShares.TryResolve(Root, Chain(depth), out var shares, out var reason);

        Assert.Equal(allowed, accepted);
        if (allowed)
        {
            Assert.Null(reason);
            Ratio(shares[Id(100 + (depth - 1) * 2)], "1", "1");
        }
        else
        {
            Assert.Empty(shares);
            Assert.Equal(InventoryCostLineageShares.DepthExceeded, reason);
        }
    }

    [Theory]
    [InlineData("mass")]
    [InlineData("input-quantity")]
    [InlineData("output-quantity")]
    [InlineData("input-identity")]
    [InlineData("output-identity")]
    [InlineData("duplicate-input")]
    [InlineData("duplicate-output")]
    [InlineData("same-label")]
    [InlineData("kind")]
    [InlineData("shape")]
    [InlineData("event-identity")]
    [InlineData("null-row")]
    public void Malformed_transformations_are_refused_without_partial_shares(string fault)
    {
        var inputs = new[] { Mass(Root, 2m) };
        var outputs = new[] { Mass(Left, 1m), Mass(Right, 1m) };
        var kind = InventoryLabelTransformationKind.Split;
        var eventId = Id(10);
        switch (fault)
        {
            case "mass": outputs[1] = Mass(Right, 2m); break;
            case "input-quantity": inputs[0] = Mass(Root, 0m); break;
            case "output-quantity": outputs[1] = Mass(Right, -1m); break;
            case "input-identity": inputs[0] = Mass(Guid.Empty, 2m); break;
            case "output-identity": outputs[1] = Mass(Guid.Empty, 1m); break;
            case "duplicate-input": inputs = [Mass(Root, 1m), Mass(Root, 1m)]; kind = InventoryLabelTransformationKind.Merge;
                outputs = [Mass(Merged, 2m)]; break;
            case "duplicate-output": outputs[1] = Mass(Left, 1m); break;
            case "same-label": outputs[1] = Mass(Root, 1m); break;
            case "kind": kind = (InventoryLabelTransformationKind)999; break;
            case "shape": outputs = [Mass(Left, 2m)]; break;
            case "event-identity": eventId = Guid.Empty; break;
            case "null-row": outputs[1] = null!; break;
        }

        Refused([new InventoryCostLineageTransformation(eventId, kind, inputs, outputs)],
            InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void Duplicate_event_identity_is_refused_before_applying_either_event()
    {
        Refused([
            Split(10, Mass(Root, 2m), [Mass(Left, 1m), Mass(Right, 1m)]),
            Split(10, Mass(Other, 2m), [Mass(Third, 1m), Mass(Merged, 1m)]),
        ], InventoryCostLineageShares.InvalidGraph);
    }

    [Fact]
    public void Transformation_enumeration_is_bounded_and_never_truncated_into_a_success()
    {
        var transformation = Split(10, Mass(Root, 2m), [Mass(Left, 1m), Mass(Right, 1m)]);

        Refused(Enumerable.Repeat(transformation, InventoryCostLineageShares.MaximumTransformations + 1),
            InventoryCostLineageShares.GraphLimitExceeded);
    }

    [Fact]
    public void Quantity_row_enumeration_is_bounded_and_never_truncated_into_a_success()
    {
        var outputs = Enumerable.Range(100, InventoryCostLineageShares.MaximumQuantityRows + 1)
            .Select(number => Mass(Id(number), 1m));

        Refused([Split(10, Mass(Root, InventoryCostLineageShares.MaximumQuantityRows + 1), outputs)],
            InventoryCostLineageShares.GraphLimitExceeded);
    }

    [Fact]
    public void Empty_root_or_missing_graph_is_refused_without_a_root_share()
    {
        Assert.False(InventoryCostLineageShares.TryResolve(Guid.Empty, [], out var emptyRootShares, out var emptyRootReason));
        Assert.Empty(emptyRootShares);
        Assert.Equal(InventoryCostLineageShares.InvalidGraph, emptyRootReason);
        Assert.False(InventoryCostLineageShares.TryResolve(Root, null, out var missingGraphShares, out var missingGraphReason));
        Assert.Empty(missingGraphShares);
        Assert.Equal(InventoryCostLineageShares.InvalidGraph, missingGraphReason);
    }

    [Fact]
    public void Input_collections_are_snapshotted_and_result_shares_are_read_only()
    {
        var inputs = new[] { Mass(Root, 2m) };
        var outputs = new[] { Mass(Left, 1m), Mass(Right, 1m) };
        var transformation = new InventoryCostLineageTransformation(Id(10), InventoryLabelTransformationKind.Split, inputs, outputs);
        inputs[0] = Mass(Other, 5m);
        outputs[0] = Mass(Third, 4m);

        var shares = Resolve([transformation]);

        Ratio(shares[Left], "1", "1");
        Assert.False(shares.ContainsKey(Third));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<Guid, ExactCostRatio>)shares).Add(Other, ExactCostRatio.Of(1m)));
        Assert.Throws<NotSupportedException>(() =>
        {
            ((IList<InventoryCostLineageQuantity>)transformation.Inputs)[0] = Mass(Other, 1m);
        });
    }

    private static IReadOnlyDictionary<Guid, ExactCostRatio> Resolve(IEnumerable<InventoryCostLineageTransformation> transformations)
    {
        Assert.True(InventoryCostLineageShares.TryResolve(Root, transformations, out var shares, out var reason), reason);
        Assert.Null(reason);
        return shares;
    }

    private static void Refused(IEnumerable<InventoryCostLineageTransformation> transformations, string expectedReason)
    {
        Assert.False(InventoryCostLineageShares.TryResolve(Root, transformations, out var shares, out var reason));
        Assert.Empty(shares);
        Assert.Equal(expectedReason, reason);
    }

    private static void Ratio(ExactCostRatio value, string numerator, string denominator) =>
        Assert.Equal((numerator, denominator), (value.Numerator, value.Denominator));

    private static InventoryCostLineageQuantity Mass(Guid labelId, decimal kilograms) => new(labelId, kilograms);
    private static InventoryCostLineageTransformation Split(int id, InventoryCostLineageQuantity input,
        IEnumerable<InventoryCostLineageQuantity> outputs) => new(Id(id), InventoryLabelTransformationKind.Split, [input], outputs);
    private static InventoryCostLineageTransformation Merge(int id, IEnumerable<InventoryCostLineageQuantity> inputs,
        InventoryCostLineageQuantity output) => new(Id(id), InventoryLabelTransformationKind.Merge, inputs, [output]);
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private static IEnumerable<InventoryCostLineageTransformation> Chain(int depth)
    {
        var current = Root;
        for (var index = 0; index < depth; index++)
        {
            var next = Id(100 + index * 2);
            yield return Split(10000 + index, Mass(current, depth + 1 - index),
                [Mass(next, depth - index), Mass(Id(101 + index * 2), 1m)]);
            current = next;
        }
    }
}
