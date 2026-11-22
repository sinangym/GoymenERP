using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// AK-8: pins <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> section 4 (SAT-D433, SAT-D440–SAT-D445) row by row. Common facts:
/// supplier X, one warehouse, TL; receipt order R1, R2, R3. R1 11 996,16 TL / 600 kg = 19,9936; R2 8 576,00 / 400 = 21,44;
/// R3 2 812,50 / 150 = 18,75. Lineage: L1 (R1 600) + L2 (R2 400) merge to L3 1 000; L3 splits to L4 300 + L5 700; L5 splits to
/// L6 350 + L6b 350; L6 + L7 (R3 150) merge to L8 500.
/// </summary>
public sealed class SupplierReturnLineageGoldenTests
{
    private static readonly Guid SupplierX = Id(100);
    private static readonly Guid SupplierY = Id(101);
    private static readonly Guid L1 = Id(1), L2 = Id(2), L3 = Id(3), L4 = Id(4), L5 = Id(5), L6 = Id(6), L6b = Id(66), L7 = Id(7), L8 = Id(8);
    private static readonly ExactCostRatio R1Cost = ExactCostRatio.Of(11996.16m).Divide(600m);
    private static readonly ExactCostRatio R2Cost = ExactCostRatio.Of(8576.00m).Divide(400m);
    private static readonly ExactCostRatio R3Cost = ExactCostRatio.Of(2812.50m).Divide(150m);

    private static readonly InventoryCostLineageTransformation[] Graph =
    [
        Transform(11, InventoryLabelTransformationKind.Merge, [(L1, 600m), (L2, 400m)], [(L3, 1000m)]),
        Transform(12, InventoryLabelTransformationKind.Split, [(L3, 1000m)], [(L4, 300m), (L5, 700m)]),
        Transform(13, InventoryLabelTransformationKind.Split, [(L5, 700m)], [(L6, 350m), (L6b, 350m)]),
        Transform(14, InventoryLabelTransformationKind.Merge, [(L6, 350m), (L7, 150m)], [(L8, 500m)]),
    ];

    [Fact]
    public void The_common_receipt_costs_are_exact_ratios()
    {
        Assert.Equal(19.9936m, R1Cost.Round(4));
        Assert.Equal(0, R1Cost.CompareTo(ExactCostRatio.Of(19.9936m)));
        Assert.Equal(0, R2Cost.CompareTo(ExactCostRatio.Of(21.44m)));
        Assert.Equal(0, R3Cost.CompareTo(ExactCostRatio.Of(18.75m)));
    }

    [Theory]
    [InlineData("L4", "3/5", "2/5", null)]
    [InlineData("L8", "21/50", "7/25", "3/10")]
    public void TI1_lineage_shares_follow_split_and_kilogram_weighted_merge(string label, string r1, string r2, string? r3)
    {
        var target = label == "L4" ? L4 : L8;
        var shares = SupplierReturnLineageCalculator.ResolveShares(target, [L1, L2, L7], Graph);

        Assert.NotNull(shares);
        Assert.Equal(r1, Text(shares[L1]));
        Assert.Equal(r2, Text(shares[L2]));
        if (r3 is null) Assert.False(shares.ContainsKey(L7));
        else Assert.Equal(r3, Text(shares[L7]));
    }

    [Theory]
    [InlineData("I01")]
    [InlineData("I02")]
    [InlineData("I03")]
    [InlineData("I04")]
    [InlineData("I06")]
    [InlineData("I09")]
    public void SAT_D433_golden_rows_distribute_by_lineage_share_and_value_each_receipt_at_its_own_cost(string row)
    {
        var (roots, kg, expected, cost) = row switch
        {
            "I01" => (L4Roots(), 300m, new[] { 180m, 120m }, 6171.65m),
            "I02" => (L4Roots(), 250m, new[] { 150m, 100m }, 5143.04m),
            "I03" => (L8Roots(), 100m, new[] { 42m, 28m, 30m }, 2002.55m),
            "I04" => (L4Roots(), 300m, new[] { 180m, 120m }, 6171.65m),
            "I06" => (L4Roots(), 300m, new[] { 180m, 120m }, 6171.65m),
            "I09" => (ThirdsRoots(), 100m, new[] { 33.333m, 66.667m }, 2095.79m),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
        Assert.Equal(SupplierReturnLineageMode.Lineage, SupplierReturnLineageCalculator.Classify(roots, SupplierX));

        var parts = SupplierReturnLineageCalculator.Distribute(roots, kg, pieces: null);
        Assert.Equal(expected, parts.Select(part => part.QuantityKg).ToArray());
        Assert.All(parts, part => Assert.Null(part.Pieces));

        var selections = row == "I04"
            // R1's 180 kg: 100 invoiced + 80 pending (TL: both shares carry 19,9936, TI5); R2's 120 kg wholly invoiced.
            ? new[]
            {
                Selection(L1, R1Cost, invoicedKg: 100m, pendingKg: 80m, invoicedRemaining: 240m, pendingRemaining: 360m),
                Selection(L2, R2Cost, invoicedKg: 120m, pendingKg: 0m),
            }
            : parts.Select(part => Selection(part.RootId, CostOf(part.RootId), part.QuantityKg, 0m)).ToArray();
        var result = SupplierReturnLineageCalculator.Value(parts, selections);

        Assert.Equal(SupplierReturnLineageOutcome.Accepted, result.Outcome);
        Assert.Equal(cost, result.CostTry);
        Assert.Equal(0, result.ExactDifferenceTry!.Sign);
    }

    [Fact]
    public void I01_exact_intermediate_cost_is_6171_648_before_the_single_display_rounding()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L4Roots(), 300m, null);
        var result = SupplierReturnLineageCalculator.Value(parts,
            parts.Select(part => Selection(part.RootId, CostOf(part.RootId), part.QuantityKg, 0m)).ToArray());

        Assert.Equal(0, result.ExactCostTry!.CompareTo(ExactCostRatio.Of(6171.648m)));
    }

    [Fact]
    public void I03_exact_intermediate_cost_is_2002_5512()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L8Roots(), 100m, null);
        var result = SupplierReturnLineageCalculator.Value(parts,
            parts.Select(part => Selection(part.RootId, CostOf(part.RootId), part.QuantityKg, 0m)).ToArray());

        Assert.Equal(0, result.ExactCostTry!.CompareTo(ExactCostRatio.Of(2002.5512m)));
    }

    [Fact]
    public void I05_the_remaining_pool_average_is_recomputed_from_the_exact_return_value()
    {
        var average = SupplierReturnLineageCalculator.RemainingAverage(2000m, 20.5m, 300m, ExactCostRatio.Of(6171.648m));

        Assert.Equal(20.4873m, average);
    }

    [Fact]
    public void I07_a_receipt_share_above_its_open_quantity_is_refused_and_never_moved_to_another_receipt()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L4Roots(), 300m, null);
        var result = SupplierReturnLineageCalculator.Value(parts,
        [
            Selection(L1, R1Cost, 180m, 0m) with { RemainingKg = 100m },
            Selection(L2, R2Cost, 120m, 0m),
        ]);

        Assert.Equal(SupplierReturnLineageOutcome.ReceiptLimitExceeded, result.Outcome);
        Assert.Null(result.CostTry);
        Assert.Equal(new[] { 180m, 120m }, result.Parts.Select(part => part.QuantityKg).ToArray());
    }

    [Fact]
    public void I08_an_invoice_cost_different_from_the_receipt_cost_is_a_SAT_D392_difference_and_is_refused()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L4Roots(), 300m, null);
        var result = SupplierReturnLineageCalculator.Value(parts,
        [
            Selection(L1, R1Cost, 180m, 0m) with { InvoiceUnitCostTryPerKg = ExactCostRatio.Of(12300.00m).Divide(600m) },
            Selection(L2, R2Cost, 120m, 0m),
        ]);

        Assert.Equal(SupplierReturnLineageOutcome.CostDifference, result.Outcome);
        Assert.Equal(0, result.ExactDifferenceTry!.CompareTo(ExactCostRatio.Of(91.152m)));
    }

    [Theory]
    [InlineData("I10")]
    [InlineData("I15")]
    public void SAT_D442_pieces_round_down_and_the_remainder_goes_to_the_largest_share_ties_to_the_first_receipt(string row)
    {
        var (roots, kg, pieces, expectedPieces, expectedKg) = row switch
        {
            "I10" => (L8Roots(), 100m, 10, new[] { 5, 2, 3 }, new[] { 42m, 28m, 30m }),
            "I15" => (HalvesRoots(), 50m, 5, new[] { 3, 2 }, new[] { 25m, 25m }),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };

        var parts = SupplierReturnLineageCalculator.Distribute(roots, kg, pieces);

        Assert.Equal(expectedPieces, parts.Select(part => part.Pieces!.Value).ToArray());
        Assert.Equal(expectedKg, parts.Select(part => part.QuantityKg).ToArray());
    }

    [Theory]
    [InlineData("I11", 80, 1700.00, 1640.00, 60.00)]
    [InlineData("I12", 100, 2125.00, 2050.00, 75.00)]
    [InlineData("I16", 50, 1062.50, 1025.00, 37.50)]
    public void SAT_D441_D444_D445_quantity_returns_leave_at_the_average_bill_at_the_order_price_and_refuse_the_difference(
        string row, double kg, double exit, double invoice, double difference)
    {
        var roots = row == "I11"
            // L9: wholly supplier Y's receipt R4.
            ? new[] { new SupplierReturnLineageRoot(Id(40), 1, SupplierReturnLineageRootKind.PurchaseReceipt, SupplierY, ExactCostRatio.Of(1m)) }
            // L10: half supplier X's R1, half production output P; X's half is not separated (SAT-D445).
            : new[]
            {
                new SupplierReturnLineageRoot(L1, 1, SupplierReturnLineageRootKind.PurchaseReceipt, SupplierX, ExactCostRatio.Of(0.5m)),
                new SupplierReturnLineageRoot(Id(50), 2, SupplierReturnLineageRootKind.Other, null, ExactCostRatio.Of(0.5m)),
            };
        Assert.Equal(SupplierReturnLineageMode.Quantity, SupplierReturnLineageCalculator.Classify(roots, SupplierX));

        // R5: X's receipt chosen by the preparer, kg-priced order 20,50 TL/kg, 300 kg open; warehouse average 21,25.
        var result = SupplierReturnLineageCalculator.QuantityReturn((decimal)kg, 300m, 21.25m, 20.50m);

        Assert.Equal((decimal)kg, result.QuantityKg);
        Assert.Equal((decimal)exit, result.StockExitTry);
        Assert.Equal((decimal)invoice, result.InvoiceTry);
        Assert.Equal((decimal)difference, result.DifferenceTry);
        Assert.Equal(SupplierReturnLineageOutcome.CostDifference, result.Outcome);
    }

    [Fact]
    public void I13_a_pending_share_of_a_foreign_currency_receipt_leaves_the_whole_cost_unavailable_without_a_partial_total()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L4Roots(), 300m, null);
        var result = SupplierReturnLineageCalculator.Value(parts,
        [
            Selection(L1, R1Cost, 180m, 0m),
            Selection(L2, R2Cost, 0m, 120m) with { PendingUnitCostTryPerKg = null },
        ]);

        Assert.Equal(SupplierReturnLineageOutcome.CostUnavailable, result.Outcome);
        Assert.Null(result.CostTry);
        Assert.Null(result.ExactCostTry);
    }

    [Fact]
    public void I14_a_chosen_receipt_without_enough_open_quantity_refuses_the_quantity_return()
    {
        var result = SupplierReturnLineageCalculator.QuantityReturn(80m, 79.999m, 21.25m, 20.50m);

        Assert.Equal(SupplierReturnLineageOutcome.ReceiptLimitExceeded, result.Outcome);
        Assert.Null(result.StockExitTry);
        Assert.Null(result.DifferenceTry);
    }

    [Fact]
    public void A_quantity_return_with_an_unreadable_average_takes_the_exception_path()
    {
        var result = SupplierReturnLineageCalculator.QuantityReturn(80m, 300m, null, 20.50m);

        Assert.Equal(SupplierReturnLineageOutcome.CostUnavailable, result.Outcome);
        Assert.Null(result.StockExitTry);
        Assert.Equal(1640.00m, result.InvoiceTry);
    }

    [Fact]
    public void A_label_without_any_receipt_of_the_returning_supplier_is_a_quantity_return()
    {
        var roots = new[] { new SupplierReturnLineageRoot(Id(60), 1, SupplierReturnLineageRootKind.Other, null, ExactCostRatio.Of(1m)) };

        Assert.Equal(SupplierReturnLineageMode.Quantity, SupplierReturnLineageCalculator.Classify(roots, SupplierX));
    }

    [Fact]
    public void A_selection_that_does_not_cover_the_receipt_share_is_refused_and_no_share_is_assumed()
    {
        var parts = SupplierReturnLineageCalculator.Distribute(L4Roots(), 300m, null);
        var result = SupplierReturnLineageCalculator.Value(parts, [Selection(L1, R1Cost, 100m, 0m), Selection(L2, R2Cost, 120m, 0m)]);

        Assert.Equal(SupplierReturnLineageOutcome.SelectionMismatch, result.Outcome);
        Assert.Equal(SupplierReturnLineageOutcome.SelectionMismatch,
            SupplierReturnLineageCalculator.Value(parts, [Selection(L1, R1Cost, 180m, 0m)]).Outcome);
    }

    [Fact]
    public void The_lineage_gate_is_open_in_this_train_and_the_seam_can_close_it()
    {
        Assert.True(SupplierReturnLineageGoldenV1.SupplierReturnLineageGoldenAccepted);
        Assert.True(SupplierReturnLineageGoldenV1.IsAccepted());
        Assert.False(SupplierReturnLineageGoldenV1.IsAccepted(overrideForTests: false));
    }

    private static SupplierReturnLineageRoot[] L4Roots() =>
        Roots(SupplierReturnLineageCalculator.ResolveShares(L4, [L1, L2, L7], Graph)!);

    private static SupplierReturnLineageRoot[] L8Roots() =>
        Roots(SupplierReturnLineageCalculator.ResolveShares(L8, [L1, L2, L7], Graph)!);

    // I09: R1 100 kg + R2 200 kg merged (shares 1/3, 2/3).
    private static SupplierReturnLineageRoot[] ThirdsRoots() => Roots(SupplierReturnLineageCalculator.ResolveShares(Id(9), [L1, L2],
        [Transform(21, InventoryLabelTransformationKind.Merge, [(L1, 100m), (L2, 200m)], [(Id(9), 300m)])])!);

    // I15: R1 50 kg + R2 50 kg merged (shares 1/2, 1/2).
    private static SupplierReturnLineageRoot[] HalvesRoots() => Roots(SupplierReturnLineageCalculator.ResolveShares(Id(10), [L1, L2],
        [Transform(22, InventoryLabelTransformationKind.Merge, [(L1, 50m), (L2, 50m)], [(Id(10), 100m)])])!);

    private static SupplierReturnLineageRoot[] Roots(IReadOnlyDictionary<Guid, ExactCostRatio> shares) =>
        shares.Select(pair => new SupplierReturnLineageRoot(pair.Key, Order(pair.Key), SupplierReturnLineageRootKind.PurchaseReceipt,
            SupplierX, pair.Value)).OrderBy(root => root.ReceiptOrder).ToArray();

    private static int Order(Guid root) => root == L1 ? 1 : root == L2 ? 2 : root == L7 ? 3 : throw new ArgumentOutOfRangeException(nameof(root));

    private static ExactCostRatio CostOf(Guid root) => root == L1 ? R1Cost : root == L2 ? R2Cost : root == L7 ? R3Cost
        : throw new ArgumentOutOfRangeException(nameof(root));

    private static SupplierReturnLineageReceiptSelection Selection(Guid root, ExactCostRatio cost, decimal invoicedKg, decimal pendingKg,
        decimal invoicedRemaining = 10_000m, decimal pendingRemaining = 10_000m) =>
        new(root, 10_000m, invoicedRemaining, pendingRemaining, invoicedKg, pendingKg, cost, cost, cost);

    private static InventoryCostLineageTransformation Transform(int id, InventoryLabelTransformationKind kind,
        (Guid Label, decimal Kg)[] inputs, (Guid Label, decimal Kg)[] outputs) =>
        new(Id(1000 + id), kind, inputs.Select(x => new InventoryCostLineageQuantity(x.Label, x.Kg)),
            outputs.Select(x => new InventoryCostLineageQuantity(x.Label, x.Kg)));

    private static string Text(ExactCostRatio value) => value.Numerator + "/" + value.Denominator;

    private static Guid Id(int value) => new($"00000000-0000-0000-0000-{value:D12}");
}
