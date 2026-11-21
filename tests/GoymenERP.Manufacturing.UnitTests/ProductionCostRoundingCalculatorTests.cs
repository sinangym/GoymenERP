using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D54 (SINAN_KARARLARI_2026-11-14_URETIM_EK4) as completed by URT-D78…URT-D80
/// (SINAN_KARARLARI_2026-11-15_DALGA2_EK10): the calculation runs at full precision, the recorded kg cost has 4 decimals
/// and a label's TL amount is its unrounded kg cost x kg rounded once to 2; the order's total is the sum of the lines,
/// not rounded again; the remainder is written on the good product line with the most kg (of equal kg, the smallest
/// label number), so the TL that went in equals the TL distributed, and an order with no good product line to carry a
/// remainder stays pending. A half is rounded away from zero, the one fixed rule (URT-D78): there is no rounding input.
/// The golden test uses the SAT-D326 (1) example; the other expected values are this file's own arithmetic of those
/// rules.
/// </summary>
public sealed class ProductionCostRoundingCalculatorTests
{
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GoodA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GoodB = Guid.Parse("a0000000-0000-0000-0000-00000000000b");
    private static readonly Guid GoodC = Guid.Parse("a0000000-0000-0000-0000-00000000000c");
    private static readonly Guid SecondA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    private static readonly Guid SecondB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid SecondC = Guid.Parse("b0000000-0000-0000-0000-00000000000c");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");

    private static ProductionScrapCostLabelInput Label(Guid id, OutputClass outputClass, decimal kg) =>
        Label(id, "ETK-" + id.ToString("N"), outputClass, kg);

    private static ProductionScrapCostLabelInput Label(Guid id, string labelNumber, OutputClass outputClass, decimal kg) =>
        new(id, labelNumber, outputClass, new Kilograms(kg));

    private static ScrapWeighingInput Weighing(decimal kg, ScrapPriceInput price) =>
        new(ScrapA, new Kilograms(kg), price);

    private static ScrapPriceInput Price(decimal tlPerKg) => new(ScrapPriceReadStatus.Value, tlPerKg);

    private static ProductionReceiptCostInput Cost(
        decimal good = 900m,
        decimal second = 50m,
        ScrapCostSettingStatus scrap = ScrapCostSettingStatus.Off,
        ScrapWeighingInput? weighing = null,
        bool closed = true,
        decimal issueCost = 40m) =>
        new(closed,
            [
                new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, issueCost,
                    IssueCostCurrencyInput.FunctionalCurrency),
            ],
            [],
            new Kilograms(good),
            new Kilograms(second),
            Kilograms.Zero,
            [weighing ?? Weighing(50m, Price(10m))],
            Kilograms.Zero,
            scrap,
            NonKgInputUse.None);

    private static ProductionCostRoundedResultV1 Round(ProductionReceiptCostInput cost,
        IReadOnlyList<ProductionScrapCostLabelInput> labels) =>
        ProductionCostRoundingCalculatorV1.Round(new ProductionScrapCostDistributionInput(cost, labels));

    // Synthetic rounding case: scrap 100 kg x 9.9999 = 999.99 TL; 39000.01 / 900 TL per product kg.
    private static ProductionReceiptCostInput ThirdsCost() =>
        Cost(good: 900m, second: 0m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(9.9999m)));

    [Fact]
    public void Golden_scrap_out_of_cost_records_the_kg_cost_at_four_decimals_and_the_tl_at_two()
    {
        // SAT-D326 (1) golden: 39500 / 950 = 41.578947... TL per kg is recorded as 41.5789; 600, 300 and 50 kg give
        // 24947.37, 12473.68 and 2078.95 TL, the scrap expense 500.00 TL: 40000.00 TL in, 40000.00 TL distributed.
        var result = Round(Cost(),
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(GoodB, OutputClass.GoodProduct, 300m),
                Label(SecondA, OutputClass.SecondQuality, 50m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(41.5789m, result.RecordedCostPerKg);
        Assert.Equal(40m, result.RecordedRawMaterialCostPerKg);
        Assert.Equal(40000m, result.InputTotalTl);
        Assert.Equal(500m, result.RecordedWeighedScrapExpenseTl);
        Assert.Equal(40000m, result.DistributedTotalTl);
        Assert.Equal(0m, result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Null(result.PendingCode);
        Assert.Null(result.UnreadableCode);
        Assert.Collection(result.Lines,
            line => AssertLine(line, GoodA, OutputClass.GoodProduct, 41.5789m, 24947.37m, 0m),
            line => AssertLine(line, GoodB, OutputClass.GoodProduct, 41.5789m, 12473.68m, 0m),
            line => AssertLine(line, SecondA, OutputClass.SecondQuality, 41.5789m, 2078.95m, 0m),
            line =>
            {
                Assert.Equal(ScrapA, line.OutputLabelId);
                Assert.Equal(OutputClass.Scrap, line.OutputClass);
                Assert.Null(line.RecordedUnitCostPerKg);
                Assert.Null(line.RecordedAmountTl);
                Assert.Equal(500m, line.RecordedSeparateScrapExpenseTl);
                Assert.Equal(0m, line.RemainderTl);
                Assert.Equal(ProductionScrapCostDistributionCodesV1.ScrapLabelCostNotDecided, line.NotAllocatedCode);
            });
        Assert.NotNull(result.Distribution);
        Assert.Equal(39500m / 950m, result.Distribution.Cost!.CostPerKg);
    }

    [Fact]
    public void A_label_amount_is_the_unrounded_kg_cost_times_kg_and_the_total_is_the_sum_of_the_lines()
    {
        // URT-D79: 900 kg x 41.578947... = 37421.0526... is recorded as 37421.05, not 900 x the recorded 41.5789
        // (37421.01); 50 kg give 2078.95. The total is the sum of the recorded lines and the scrap expense, not rounded
        // again.
        var result = Round(Cost(),
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(41.5789m, result.RecordedCostPerKg);
        var good = result.Lines[0];
        Assert.Equal(GoodA, good.OutputLabelId);
        Assert.Equal(37421.05m, good.RecordedAmountTl);
        Assert.NotEqual(900m * result.RecordedCostPerKg, good.RecordedAmountTl);
        Assert.Equal(37421.01m, decimal.Round(900m * result.RecordedCostPerKg!.Value, 2));
        Assert.Equal(2078.95m, result.Lines[1].RecordedAmountTl);
        Assert.Equal(0m, result.RemainderTl);
        var sum = 0m;
        foreach (var line in result.Lines)
        {
            sum += (line.RecordedAmountTl ?? 0m) + (line.RecordedSeparateScrapExpenseTl ?? 0m);
        }

        Assert.Equal(40000m, sum);
        Assert.Equal(sum, result.DistributedTotalTl);
        Assert.Equal(result.InputTotalTl, result.DistributedTotalTl);
    }

    [Fact]
    public void The_remainder_goes_to_the_good_product_line_so_the_tl_in_equals_the_tl_distributed()
    {
        // URT-D80: three 300 kg labels of 13000.00333... TL record 13000.00 each; the 0.01 TL left goes on the good
        // product line (equal kg: the smallest label number, here GoodA's), whatever order the labels are given in.
        var result = Round(ThirdsCost(),
            [Label(GoodB, OutputClass.GoodProduct, 300m), Label(GoodC, OutputClass.GoodProduct, 300m),
                Label(GoodA, OutputClass.GoodProduct, 300m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(43.3333m, result.RecordedCostPerKg);
        Assert.Equal(40000m, result.InputTotalTl);
        Assert.Equal(0m, result.RecordedWeighedScrapExpenseTl);
        Assert.Equal(0.01m, result.RemainderTl);
        Assert.Equal(GoodA, result.RemainderLabelId);
        Assert.Equal(40000m, result.DistributedTotalTl);
        Assert.Equal(result.InputTotalTl, result.DistributedTotalTl);
        Assert.Collection(result.Lines,
            line => AssertLine(line, GoodA, OutputClass.GoodProduct, 43.3333m, 13000.01m, 0.01m),
            line => AssertLine(line, GoodB, OutputClass.GoodProduct, 43.3333m, 13000m, 0m),
            line => AssertLine(line, GoodC, OutputClass.GoodProduct, 43.3333m, 13000m, 0m),
            line =>
            {
                Assert.Equal(ScrapA, line.OutputLabelId);
                Assert.Equal(999.99m, line.RecordedAmountTl);
                Assert.Null(line.RecordedSeparateScrapExpenseTl);
                Assert.Equal(0m, line.RemainderTl);
            });
    }

    [Fact]
    public void Of_equal_kg_the_smallest_label_number_carries_the_remainder_not_the_smallest_id()
    {
        // URT-D80 tie: ETK-9 is the smallest of ETK-9, ETK-10 and ETK-11 (the shorter number first), so GoodB carries
        // the 0.01 TL although GoodA has the smallest id and an ordinal compare would put ETK-10 first.
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
        [
            Label(GoodA, "ETK-10", OutputClass.GoodProduct, 300m), Label(GoodB, "ETK-9", OutputClass.GoodProduct, 300m),
            Label(GoodC, "ETK-11", OutputClass.GoodProduct, 300m),
        ];

        var forward = Round(ThirdsCost(), labels);
        var reversed = Round(ThirdsCost(), labels.Reverse().ToArray());

        Assert.Equal(ProductionReceiptCostStatus.Value, forward.Status);
        Assert.Equal(GoodB, forward.RemainderLabelId);
        Assert.Equal(0.01m, forward.RemainderTl);
        Assert.Collection(forward.Lines,
            line => AssertLine(line, GoodA, OutputClass.GoodProduct, 43.3333m, 13000m, 0m),
            line => AssertLine(line, GoodB, OutputClass.GoodProduct, 43.3333m, 13000.01m, 0.01m),
            line => AssertLine(line, GoodC, OutputClass.GoodProduct, 43.3333m, 13000m, 0m),
            line => Assert.Equal(ScrapA, line.OutputLabelId));
        Assert.Equal(40000m, forward.DistributedTotalTl);
        Assert.Equal(forward.Lines, reversed.Lines);
        Assert.Equal(forward.RemainderLabelId, reversed.RemainderLabelId);
    }

    [Fact]
    public void Of_several_good_product_lines_the_one_with_the_most_kg_carries_the_remainder()
    {
        // 100 + 700 + 100 kg: 4333.33 + 30333.34 + 4333.33 = 39000.00, so 0.01 TL goes on the 700 kg line, whose label
        // number is not the smallest.
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
        [
            Label(GoodA, OutputClass.GoodProduct, 100m), Label(GoodB, OutputClass.GoodProduct, 700m),
            Label(GoodC, OutputClass.GoodProduct, 100m),
        ];

        var forward = Round(ThirdsCost(), labels);
        var reversed = Round(ThirdsCost(), labels.Reverse().ToArray());

        Assert.Equal(GoodB, forward.RemainderLabelId);
        Assert.Equal(0.01m, forward.RemainderTl);
        Assert.Equal(30333.35m, forward.Lines[1].RecordedAmountTl);
        Assert.Equal(4333.33m, forward.Lines[0].RecordedAmountTl);
        Assert.Equal(40000m, forward.DistributedTotalTl);
        Assert.Equal(forward.Lines, reversed.Lines);
        Assert.Equal(forward.RemainderLabelId, reversed.RemainderLabelId);
    }

    [Fact]
    public void A_remainder_with_no_good_product_line_leaves_the_cost_pending_and_is_not_moved_to_another_class()
    {
        // URT-D80: "Emirde iyi urun etiketi yoksa maliyet 'bekliyor' kalir"; an order of second quality only has none.
        var result = Round(
            Cost(good: 0m, second: 900m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(9.9999m))),
            [Label(SecondA, OutputClass.SecondQuality, 300m), Label(SecondB, OutputClass.SecondQuality, 300m),
                Label(SecondC, OutputClass.SecondQuality, 300m)]);

        AssertPending(result, ProductionCostRoundingCodesV1.RemainderLineMissing);
        Assert.Equal("rounding_remainder_line_missing", result.PendingCode);
    }

    [Fact]
    public void No_remainder_needs_no_good_product_line()
    {
        // 39000 TL goes to product and 1000 TL to scrap, with no remainder.
        var result = Round(
            Cost(good: 0m, second: 900m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(10m))),
            [Label(SecondA, OutputClass.SecondQuality, 900m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(0m, result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Equal(39000m, result.Lines[0].RecordedAmountTl);
        Assert.Equal(40000m, result.DistributedTotalTl);
    }

    [Fact]
    public void A_half_tl_amount_is_rounded_away_from_zero()
    {
        // URT-D78: 1000 kg x 40.000125 TL = 40000.125 TL in is recorded as 40000.13 (to even would give 40000.12); the
        // kg cost 40.000125 is 40.0001.
        var result = Round(
            Cost(good: 1000m, second: 0m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(0m, ScrapPriceInput.Missing),
                issueCost: 40.000125m),
            [Label(GoodA, OutputClass.GoodProduct, 1000m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000.13m, result.InputTotalTl);
        Assert.Equal(40000.13m, result.Lines[0].RecordedAmountTl);
        Assert.Equal(40.0001m, result.RecordedCostPerKg);
        Assert.Equal(0m, result.RemainderTl);
        Assert.Equal(40000.13m, result.DistributedTotalTl);
    }

    [Fact]
    public void A_half_kg_cost_is_rounded_away_from_zero()
    {
        // URT-D78: 1000 kg x 40.00005 TL: the kg cost 40.00005 is recorded as 40.0001 (to even would give 40.0000); the
        // 40000.05 TL in needs no rounding.
        var result = Round(
            Cost(good: 1000m, second: 0m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(0m, ScrapPriceInput.Missing),
                issueCost: 40.00005m),
            [Label(GoodA, OutputClass.GoodProduct, 1000m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40.0001m, result.RecordedCostPerKg);
        Assert.Equal(40.0001m, result.Lines[0].RecordedUnitCostPerKg);
        Assert.Equal(40000.05m, result.InputTotalTl);
        Assert.Equal(40000.05m, result.Lines[0].RecordedAmountTl);
        Assert.Equal(0m, result.RemainderTl);
    }

    [Fact]
    public void A_missing_input_is_unreadable_without_throwing()
    {
        AssertUnreadable(ProductionCostRoundingCalculatorV1.Round(null!), ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(
            ProductionCostRoundingCalculatorV1.Round(new ProductionScrapCostDistributionInput(Cost(), null!)),
            ProductionReceiptCostCodesV1.InputInvalid);
    }

    [Fact]
    public void An_open_order_is_pending()
    {
        IReadOnlyList<ProductionScrapCostLabelInput> labels = [Label(GoodA, OutputClass.GoodProduct, 950m)];

        AssertPending(Round(Cost(closed: false), labels), ProductionReceiptCostCodesV1.OrderNotClosed);
    }

    [Fact]
    public void A_pending_distribution_stays_pending()
    {
        // 50 kg weighed scrap with no price entered for its month (URT-D47): pending.
        var cost = Cost(weighing: Weighing(50m, ScrapPriceInput.Missing));
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)];

        AssertPending(Round(cost, labels), ProductionReceiptCostCodesV1.ScrapPriceMissing);
    }

    [Fact]
    public void A_zero_issue_cost_is_carried_through_with_the_issue_named()
    {
        // URT-D58 (1): the zero-cost issue is named.
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)];

        var result = Round(Cost(issueCost: 0m), labels);

        AssertUnreadable(result, ProductionReceiptCostCodesV1.InputCostZero);
        Assert.Equal([new ProductionReceiptCostIssueReasonV1(IssueA, ProductionReceiptCostCodesV1.InputCostZero)],
            result.IssueReasons);
    }

    [Fact]
    public void The_recorded_scales_and_the_half_rule_are_the_decided_ones()
    {
        Assert.Equal(4, ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale);
        Assert.Equal(2, ProductionCostRoundingCalculatorV1.RecordedAmountScale);
        Assert.Equal(1, ProductionCostRoundingCalculatorV1.ContractVersion);
        Assert.Equal(MidpointRounding.AwayFromZero, ProductionCostRoundingCalculatorV1.HalfRounding);
        Assert.Equal(0.13m, decimal.Round(0.125m, 2, ProductionCostRoundingCalculatorV1.HalfRounding));
        Assert.Equal(-0.13m, decimal.Round(-0.125m, 2, ProductionCostRoundingCalculatorV1.HalfRounding));
    }

    [Fact]
    public void Label_numbers_compare_shorter_first_then_ordinal()
    {
        Assert.True(ProductionCostRoundingCalculatorV1.CompareLabelNumbers("ETK-9", "ETK-10") < 0);
        Assert.True(ProductionCostRoundingCalculatorV1.CompareLabelNumbers("ETK-10", "ETK-9") > 0);
        Assert.True(ProductionCostRoundingCalculatorV1.CompareLabelNumbers("ETK-10", "ETK-11") < 0);
        Assert.True(ProductionCostRoundingCalculatorV1.CompareLabelNumbers("ETK-B", "etk-a") < 0);
        Assert.Equal(0, ProductionCostRoundingCalculatorV1.CompareLabelNumbers("ETK-10", "ETK-10"));
    }

    private static void AssertLine(ProductionCostRoundedLineV1 line, Guid id, OutputClass outputClass,
        decimal unitCost, decimal amount, decimal remainder)
    {
        Assert.Equal(id, line.OutputLabelId);
        Assert.Equal(outputClass, line.OutputClass);
        Assert.Equal(unitCost, line.RecordedUnitCostPerKg);
        Assert.Equal(amount, line.RecordedAmountTl);
        Assert.Null(line.RecordedSeparateScrapExpenseTl);
        Assert.Equal(remainder, line.RemainderTl);
        Assert.Null(line.NotAllocatedCode);
    }

    private static void AssertUnreadable(ProductionCostRoundedResultV1 result, string code)
    {
        Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
        Assert.Equal(code, result.UnreadableCode);
        Assert.Null(result.PendingCode);
        AssertNoFigure(result);
    }

    private static void AssertPending(ProductionCostRoundedResultV1 result, string code)
    {
        Assert.Equal(ProductionReceiptCostStatus.Pending, result.Status);
        Assert.Equal(code, result.PendingCode);
        Assert.Null(result.UnreadableCode);
        Assert.Empty(result.IssueReasons);
        AssertNoFigure(result);
    }

    private static void AssertNoFigure(ProductionCostRoundedResultV1 result)
    {
        Assert.Null(result.Distribution);
        Assert.Null(result.RecordedCostPerKg);
        Assert.Null(result.RecordedRawMaterialCostPerKg);
        Assert.Null(result.InputTotalTl);
        Assert.Null(result.RecordedWeighedScrapExpenseTl);
        Assert.Null(result.DistributedTotalTl);
        Assert.Null(result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Empty(result.Lines);
    }
}
