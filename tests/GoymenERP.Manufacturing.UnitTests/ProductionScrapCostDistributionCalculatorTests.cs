using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D307 scrap cost distribution over the order's output labels: good product and second quality labels carry the
/// same cost per kg (SAT-D307 (1)-(2)); a usable remnant label returns at the net raw-material kg cost (URT-D46); a scrap
/// label carries no amount because its stock cost is not decided (URT-D8), and with scrap out of cost it shows its share
/// of the separate scrap expense (SAT-D316 (1), URT-D47). The golden tests use the literal numbers of SAT-D326 (1) and
/// URT-D46 (#66 6857496471 (1), #66 6864631364 (14)); nothing is rounded (URT-S14), so the expected values are the same
/// unrounded decimal arithmetic, and the rounded checks only anchor the magnitude of the golden examples.
/// </summary>
public sealed class ProductionScrapCostDistributionCalculatorTests
{
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GoodA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GoodB = Guid.Parse("a0000000-0000-0000-0000-00000000000b");
    private static readonly Guid SecondA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    private static readonly Guid RemnantA = Guid.Parse("f0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ScrapB = Guid.Parse("e0000000-0000-0000-0000-00000000000b");

    private static ProductionScrapCostLabelInput Label(Guid id, OutputClass outputClass, decimal kg) =>
        new(id, outputClass, new Kilograms(kg));

    private static ScrapPriceInput Price(decimal tlPerKg) => new(ScrapPriceReadStatus.Value, tlPerKg);

    private static ScrapWeighingInput Weighing(Guid id, decimal kg, ScrapPriceInput price) =>
        new(id, new Kilograms(kg), price);

    private static ProductionReceiptCostInput Cost(
        decimal good = 900m,
        decimal second = 50m,
        decimal remnant = 0m,
        ScrapCostSettingStatus scrap = ScrapCostSettingStatus.Off,
        IReadOnlyList<ScrapWeighingInput>? weighings = null,
        bool closed = true,
        decimal issueCost = 40m,
        NonKgInputUse nonKg = NonKgInputUse.None) =>
        new(closed,
            [new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, issueCost)],
            [],
            new Kilograms(good),
            new Kilograms(second),
            new Kilograms(remnant),
            weighings ?? [Weighing(ScrapA, 50m, Price(10m))],
            Kilograms.Zero,
            scrap,
            nonKg);

    private static IReadOnlyList<ProductionScrapCostLabelInput> GoldenLabels() =>
        [Label(GoodA, OutputClass.GoodProduct, 600m), Label(GoodB, OutputClass.GoodProduct, 300m),
            Label(SecondA, OutputClass.SecondQuality, 50m)];

    private static ProductionScrapCostDistributionResult Distribute(
        ProductionReceiptCostInput cost, IReadOnlyList<ProductionScrapCostLabelInput> labels) =>
        ProductionScrapCostDistributionCalculatorV1.Distribute(new ProductionScrapCostDistributionInput(cost, labels));

    [Fact]
    public void Golden_scrap_out_of_cost_gives_good_and_second_quality_labels_the_same_cost_per_kg_and_scrap_no_amount()
    {
        // SAT-D326 (1) golden: 1000 kg x 40 TL, 900 kg good (600 + 300) + 50 kg second quality, 50 kg weighed scrap x
        // 10 TL = 500 TL separate expense; cost per kg 39500 / 950 = 41.578947... TL.
        var result = Distribute(Cost(), GoldenLabels());

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Null(result.PendingCode);
        Assert.Null(result.UnreadableCode);
        var costPerKg = 39500m / 950m;
        Assert.Equal(41.578947m, decimal.Round(costPerKg, 6));
        Assert.Collection(result.Lines,
            line => AssertAllocated(line, GoodA, OutputClass.GoodProduct, 600m, costPerKg,
                ProductionScrapCostDistributionCodesV1.AllocatedAtProductCost),
            line => AssertAllocated(line, GoodB, OutputClass.GoodProduct, 300m, costPerKg,
                ProductionScrapCostDistributionCodesV1.AllocatedAtProductCost),
            line => AssertAllocated(line, SecondA, OutputClass.SecondQuality, 50m, costPerKg,
                ProductionScrapCostDistributionCodesV1.AllocatedAtProductCost),
            line => AssertScrap(line, ScrapA, 50m, 500m));
        Assert.Equal(24947.368421m, decimal.Round(result.Lines[0].AllocatedCost!.Value, 6));
        Assert.Equal(39500m, decimal.Round(result.AllocatedTotal!.Value, 10));
        Assert.Equal(500m, result.Cost!.WeighedScrapExpense);
    }

    [Fact]
    public void Golden_scrap_in_cost_loads_the_whole_net_cost_on_good_and_second_quality_labels()
    {
        // SAT-D307 (1)-(3): 40000 / 950 per kg; the scrap weighing carries neither an amount nor an expense.
        var result = Distribute(Cost(scrap: ScrapCostSettingStatus.On), GoldenLabels());

        var costPerKg = 40000m / 950m;
        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(costPerKg, result.Lines[0].UnitCostPerKg);
        Assert.Equal(600m * costPerKg, result.Lines[0].AllocatedCost);
        Assert.Equal(50m * costPerKg, result.Lines[2].AllocatedCost);
        AssertScrap(result.Lines[3], ScrapA, 50m, null);
        Assert.Equal(40000m, decimal.Round(result.AllocatedTotal!.Value, 10));
    }

    [Fact]
    public void Golden_usable_remnant_label_returns_at_the_net_raw_material_cost_and_leaves_the_product_divisor()
    {
        // URT-D46 golden: 1000 kg x 40 TL, 900 kg good, 50 kg remnant, 50 kg weighed scrap x 10 TL:
        // remnant 50 x 40 = 2000 TL, product (40000 - 2000 - 500) / 900 = 41.67 TL per kg.
        var result = Distribute(
            Cost(second: 0m, remnant: 50m),
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(RemnantA, OutputClass.UsableRemnant, 50m)]);

        var costPerKg = 37500m / 900m;
        Assert.Equal(41.67m, decimal.Round(costPerKg, 2));
        Assert.Collection(result.Lines,
            line => AssertAllocated(line, GoodA, OutputClass.GoodProduct, 900m, costPerKg,
                ProductionScrapCostDistributionCodesV1.AllocatedAtProductCost),
            line => AssertAllocated(line, RemnantA, OutputClass.UsableRemnant, 50m, 40m,
                ProductionScrapCostDistributionCodesV1.AllocatedAtRawMaterialCost),
            line => AssertScrap(line, ScrapA, 50m, 500m));
        Assert.Equal(2000m, result.Lines[1].AllocatedCost);
        Assert.Equal(result.Cost!.UsableRemnantValue, result.Lines[1].AllocatedCost);
        Assert.Equal(39500m, decimal.Round(result.AllocatedTotal!.Value, 10));
    }

    [Fact]
    public void Usable_remnant_with_scrap_in_cost_returns_the_same_way_and_the_product_bears_the_scrap()
    {
        // #66 6874860782 (SINAN-CEVAP (10)): the same order with scrap in cost, (40000 - 2000) / 900 = 42.22 TL per kg.
        var result = Distribute(
            Cost(second: 0m, remnant: 50m, scrap: ScrapCostSettingStatus.On),
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(RemnantA, OutputClass.UsableRemnant, 50m)]);

        var costPerKg = 38000m / 900m;
        Assert.Equal(42.22m, decimal.Round(costPerKg, 2));
        Assert.Equal(900m * costPerKg, result.Lines[0].AllocatedCost);
        Assert.Equal(2000m, result.Lines[1].AllocatedCost);
        AssertScrap(result.Lines[2], ScrapA, 50m, null);
    }

    [Fact]
    public void Each_scrap_weighing_shows_its_own_share_of_the_separate_scrap_expense_and_no_stock_cost()
    {
        // URT-D47: each weighing at its own month's price; 30 kg x 10 + 20 kg x 12 = 540 TL; a zero-kg weighing with no
        // price entered adds nothing (SAT-D326 (4)).
        var weighings = new[]
        {
            Weighing(ScrapB, 20m, Price(12m)), Weighing(ScrapA, 30m, Price(10m)),
            Weighing(Guid.Parse("e0000000-0000-0000-0000-00000000000c"), 0m, ScrapPriceInput.Missing),
        };

        var result = Distribute(Cost(weighings: weighings), GoldenLabels());

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        var scrapLines = result.Lines.Where(line => line.OutputClass == OutputClass.Scrap).ToArray();
        Assert.Equal(new[] { 300m, 240m, 0m }, scrapLines.Select(line => line.SeparateScrapExpense!.Value));
        Assert.All(scrapLines, line => Assert.Null(line.AllocatedCost));
        Assert.Equal(540m, scrapLines.Sum(line => line.SeparateScrapExpense!.Value));
        Assert.Equal(result.Cost!.WeighedScrapExpense, scrapLines.Sum(line => line.SeparateScrapExpense!.Value));
        Assert.Equal(39460m / 950m, result.Lines[0].UnitCostPerKg);
    }

    [Fact]
    public void An_open_order_is_pending_before_its_labels_are_checked()
    {
        // SAT-D317 (1): no amount until the order is closed; its labels are not final, so a mismatch is not unreadable.
        var result = Distribute(Cost(closed: false), [Label(GoodA, OutputClass.GoodProduct, 1m)]);

        Assert.Equal(ProductionReceiptCostStatus.Pending, result.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.OrderNotClosed, result.PendingCode);
        Assert.Empty(result.Lines);
        Assert.Null(result.AllocatedTotal);
        Assert.Null(result.Cost);
    }

    [Fact]
    public void A_missing_scrap_price_leaves_the_distribution_pending_with_no_line()
    {
        var result = Distribute(Cost(weighings: [Weighing(ScrapA, 50m, ScrapPriceInput.Missing)]), GoldenLabels());

        Assert.Equal(ProductionReceiptCostStatus.Pending, result.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.ScrapPriceMissing, result.PendingCode);
        Assert.Empty(result.Lines);
        Assert.Null(result.AllocatedTotal);
    }

    [Fact]
    public void Labels_that_do_not_add_up_to_their_class_kilograms_are_unreadable_and_win_over_a_missing_scrap_price()
    {
        IReadOnlyList<ProductionScrapCostLabelInput> shortLabels =
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(SecondA, OutputClass.SecondQuality, 50m)];
        IReadOnlyList<ProductionScrapCostLabelInput> wrongClass =
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(GoodB, OutputClass.SecondQuality, 300m),
                Label(SecondA, OutputClass.GoodProduct, 50m)];

        foreach (var labels in new[] { shortLabels, wrongClass })
        {
            var result = Distribute(Cost(), labels);
            Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
            Assert.Equal(ProductionScrapCostDistributionCodesV1.LabelMassMismatch, result.UnreadableCode);
            Assert.Empty(result.Lines);
        }

        var pendingPrice = Distribute(Cost(weighings: [Weighing(ScrapA, 50m, ScrapPriceInput.Missing)]), shortLabels);
        Assert.Equal(ProductionScrapCostDistributionCodesV1.LabelMassMismatch, pendingPrice.UnreadableCode);
    }

    [Fact]
    public void A_label_id_used_twice_or_also_as_a_scrap_output_id_is_unreadable()
    {
        IReadOnlyList<ProductionScrapCostLabelInput> twice =
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(GoodA, OutputClass.GoodProduct, 300m),
                Label(SecondA, OutputClass.SecondQuality, 50m)];
        IReadOnlyList<ProductionScrapCostLabelInput> asScrap =
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(ScrapA, OutputClass.GoodProduct, 300m),
                Label(SecondA, OutputClass.SecondQuality, 50m)];

        foreach (var labels in new[] { twice, asScrap })
        {
            var result = Distribute(Cost(), labels);
            Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
            Assert.Equal(ProductionScrapCostDistributionCodesV1.LabelDuplicate, result.UnreadableCode);
        }
    }

    [Theory]
    [InlineData(OutputClass.Scrap)]
    [InlineData(OutputClass.ProcessLoss)]
    [InlineData((OutputClass)99)]
    public void A_scrap_process_loss_or_undefined_class_label_is_unreadable(OutputClass outputClass)
    {
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [.. GoldenLabels(), Label(Guid.Parse("a0000000-0000-0000-0000-00000000000c"), outputClass, 0m)];

        var result = Distribute(Cost(), labels);

        Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
        Assert.Equal(ProductionScrapCostDistributionCodesV1.LabelInvalid, result.UnreadableCode);
    }

    [Fact]
    public void A_missing_input_label_list_or_label_is_unreadable_without_throwing()
    {
        Assert.Equal(ProductionReceiptCostCodesV1.InputInvalid,
            ProductionScrapCostDistributionCalculatorV1.Distribute(null!).UnreadableCode);
        Assert.Equal(ProductionReceiptCostCodesV1.InputInvalid,
            ProductionScrapCostDistributionCalculatorV1.Distribute(new ProductionScrapCostDistributionInput(null!, []))
                .UnreadableCode);
        Assert.Equal(ProductionReceiptCostCodesV1.InputInvalid, Distribute(Cost(), null!).UnreadableCode);
        Assert.Equal(ProductionScrapCostDistributionCodesV1.LabelInvalid,
            Distribute(Cost(), [.. GoldenLabels(), null!]).UnreadableCode);
    }

    [Fact]
    public void An_unreadable_cost_is_carried_through_with_its_code_and_no_line()
    {
        var result = Distribute(Cost(issueCost: 0m), GoldenLabels());

        Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.InputCostUnreadable, result.UnreadableCode);
        Assert.Empty(result.Lines);
        Assert.Null(result.AllocatedTotal);
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_of_labels_and_weighings()
    {
        var weighings = new[] { Weighing(ScrapA, 30m, Price(10m)), Weighing(ScrapB, 20m, Price(12m)) };
        var labels = GoldenLabels();

        var forward = Distribute(Cost(weighings: weighings), labels);
        var reversed = Distribute(Cost(weighings: Enumerable.Reverse(weighings).ToArray()),
            Enumerable.Reverse(labels).ToArray());

        Assert.Equal(forward.Lines, reversed.Lines);
        Assert.Equal(forward.AllocatedTotal, reversed.AllocatedTotal);
        Assert.Equal(new[] { GoodA, GoodB, SecondA, ScrapA, ScrapB }, forward.Lines.Select(line => line.OutputLabelId));
    }

    [Fact]
    public void The_distribution_contract_is_version_one()
    {
        Assert.Equal(1, ProductionScrapCostDistributionCalculatorV1.ContractVersion);
    }

    private static void AssertAllocated(ProductionScrapCostDistributionLine line, Guid id, OutputClass outputClass,
        decimal kg, decimal unitCost, string basis)
    {
        Assert.Equal(id, line.OutputLabelId);
        Assert.Equal(outputClass, line.OutputClass);
        Assert.Equal(kg, line.Kilograms);
        Assert.Equal(unitCost, line.UnitCostPerKg);
        Assert.Equal(kg * unitCost, line.AllocatedCost);
        Assert.Null(line.SeparateScrapExpense);
        Assert.Equal(basis, line.AllocationBasis);
        Assert.Null(line.NotAllocatedCode);
    }

    private static void AssertScrap(ProductionScrapCostDistributionLine line, Guid id, decimal kg, decimal? expense)
    {
        Assert.Equal(id, line.OutputLabelId);
        Assert.Equal(OutputClass.Scrap, line.OutputClass);
        Assert.Equal(kg, line.Kilograms);
        Assert.Null(line.UnitCostPerKg);
        Assert.Null(line.AllocatedCost);
        Assert.Equal(expense, line.SeparateScrapExpense);
        Assert.Null(line.AllocationBasis);
        Assert.Equal(ProductionScrapCostDistributionCodesV1.ScrapLabelCostNotDecided, line.NotAllocatedCode);
    }
}
