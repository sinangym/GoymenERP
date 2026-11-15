using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D54 (SINAN_KARARLARI_2026-11-14_URETIM_EK4): the calculation runs at full precision, the recorded kg cost has 4
/// decimals and the recorded TL 2, each rounded once; the remainder is written on the good product line, so the TL that
/// went in equals the TL distributed. The direction of a half is not decided (URT-D54 (3)), so it is an explicit input
/// and "not decided" leaves the cost pending. The golden test uses the SAT-D326 (1) example; the other expected values
/// are this file's own arithmetic of those rules.
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

    private static readonly ProductionCostRoundingPolicyInputV1 AwayFromZero =
        new(ProductionCostRoundingPolicyStatusV1.Value, ProductionCostRoundingDirectionV1.HalfAwayFromZero);

    private static readonly ProductionCostRoundingPolicyInputV1 ToEven =
        new(ProductionCostRoundingPolicyStatusV1.Value, ProductionCostRoundingDirectionV1.HalfToEven);

    private static ProductionScrapCostLabelInput Label(Guid id, OutputClass outputClass, decimal kg) =>
        new(id, outputClass, new Kilograms(kg));

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
        IReadOnlyList<ProductionScrapCostLabelInput> labels, ProductionCostRoundingPolicyInputV1? policy = null) =>
        ProductionCostRoundingCalculatorV1.Round(new ProductionCostRoundingInputV1(
            new ProductionScrapCostDistributionInput(cost, labels), policy ?? AwayFromZero));

    // 1000 kg x 40 TL, scrap in cost, 900 kg good and 100 kg weighed scrap: 40000 / 900 = 44.444... TL per kg.
    private static ProductionReceiptCostInput ThirdsCost() =>
        Cost(good: 900m, second: 0m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(10m)));

    [Theory]
    [InlineData(ProductionCostRoundingDirectionV1.HalfAwayFromZero)]
    [InlineData(ProductionCostRoundingDirectionV1.HalfToEven)]
    public void Golden_scrap_out_of_cost_records_the_kg_cost_at_four_decimals_and_the_tl_at_two(
        ProductionCostRoundingDirectionV1 direction)
    {
        // SAT-D326 (1) golden: 39500 / 950 = 41.578947... TL per kg is recorded as 41.5789; 600, 300 and 50 kg give
        // 24947.37, 12473.68 and 2078.95 TL, the scrap expense 500.00 TL: 40000.00 TL in, 40000.00 TL distributed.
        var result = Round(Cost(),
            [Label(GoodA, OutputClass.GoodProduct, 600m), Label(GoodB, OutputClass.GoodProduct, 300m),
                Label(SecondA, OutputClass.SecondQuality, 50m)],
            new ProductionCostRoundingPolicyInputV1(ProductionCostRoundingPolicyStatusV1.Value, direction));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(direction, result.Direction);
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
    public void The_remainder_goes_to_the_good_product_line_so_the_tl_in_equals_the_tl_distributed()
    {
        // URT-D54 (2): three 300 kg labels of 13333.333... TL record 13333.33 each; the 0.01 TL left goes on the good
        // product line (equal kg: the lowest label id).
        var result = Round(ThirdsCost(),
            [Label(GoodB, OutputClass.GoodProduct, 300m), Label(GoodC, OutputClass.GoodProduct, 300m),
                Label(GoodA, OutputClass.GoodProduct, 300m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(44.4444m, result.RecordedCostPerKg);
        Assert.Equal(40000m, result.InputTotalTl);
        Assert.Equal(0m, result.RecordedWeighedScrapExpenseTl);
        Assert.Equal(0.01m, result.RemainderTl);
        Assert.Equal(GoodA, result.RemainderLabelId);
        Assert.Equal(40000m, result.DistributedTotalTl);
        Assert.Equal(result.InputTotalTl, result.DistributedTotalTl);
        Assert.Collection(result.Lines,
            line => AssertLine(line, GoodA, OutputClass.GoodProduct, 44.4444m, 13333.34m, 0.01m),
            line => AssertLine(line, GoodB, OutputClass.GoodProduct, 44.4444m, 13333.33m, 0m),
            line => AssertLine(line, GoodC, OutputClass.GoodProduct, 44.4444m, 13333.33m, 0m),
            line =>
            {
                Assert.Equal(ScrapA, line.OutputLabelId);
                Assert.Null(line.RecordedAmountTl);
                Assert.Null(line.RecordedSeparateScrapExpenseTl);
                Assert.Equal(0m, line.RemainderTl);
            });
    }

    [Fact]
    public void Of_several_good_product_lines_the_one_with_the_most_kg_carries_the_remainder()
    {
        // 100 + 700 + 100 kg: 4444.44 + 31111.11 + 4444.44 = 39999.99, so 0.01 TL goes on the 700 kg line.
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
        [
            Label(GoodA, OutputClass.GoodProduct, 100m), Label(GoodB, OutputClass.GoodProduct, 700m),
            Label(GoodC, OutputClass.GoodProduct, 100m),
        ];

        var forward = Round(ThirdsCost(), labels);
        var reversed = Round(ThirdsCost(), labels.Reverse().ToArray());

        Assert.Equal(GoodB, forward.RemainderLabelId);
        Assert.Equal(0.01m, forward.RemainderTl);
        Assert.Equal(31111.12m, forward.Lines[1].RecordedAmountTl);
        Assert.Equal(4444.44m, forward.Lines[0].RecordedAmountTl);
        Assert.Equal(40000m, forward.DistributedTotalTl);
        Assert.Equal(forward.Lines, reversed.Lines);
        Assert.Equal(forward.RemainderLabelId, reversed.RemainderLabelId);
    }

    [Fact]
    public void A_remainder_with_no_good_product_line_is_unreadable_and_not_moved_to_another_class()
    {
        // URT-D54 (2) names the good product line; an order of second quality only has none.
        var result = Round(
            Cost(good: 0m, second: 900m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(10m))),
            [Label(SecondA, OutputClass.SecondQuality, 300m), Label(SecondB, OutputClass.SecondQuality, 300m),
                Label(SecondC, OutputClass.SecondQuality, 300m)]);

        AssertUnreadable(result, ProductionCostRoundingCodesV1.RemainderLineMissing);
    }

    [Fact]
    public void No_remainder_needs_no_good_product_line()
    {
        // 900 kg x 40000 / 900 = 40000.00 exactly.
        var result = Round(
            Cost(good: 0m, second: 900m, scrap: ScrapCostSettingStatus.On, weighing: Weighing(100m, Price(10m))),
            [Label(SecondA, OutputClass.SecondQuality, 900m)]);

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(0m, result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Equal(40000m, result.Lines[0].RecordedAmountTl);
        Assert.Equal(40000m, result.DistributedTotalTl);
    }

    [Fact]
    public void The_direction_of_a_half_is_the_given_one()
    {
        // 1000 kg x 40.000125 TL = 40000.125 TL in: away from zero 40000.13, to even 40000.12; the kg cost
        // 40.000125 is 40.0001 either way.
        var cost = Cost(good: 1000m, second: 0m, scrap: ScrapCostSettingStatus.On,
            weighing: Weighing(0m, ScrapPriceInput.Missing), issueCost: 40.000125m);
        IReadOnlyList<ProductionScrapCostLabelInput> labels = [Label(GoodA, OutputClass.GoodProduct, 1000m)];

        var away = Round(cost, labels, AwayFromZero);
        var even = Round(cost, labels, ToEven);

        Assert.Equal(40000.13m, away.InputTotalTl);
        Assert.Equal(40000.13m, away.Lines[0].RecordedAmountTl);
        Assert.Equal(40.0001m, away.RecordedCostPerKg);
        Assert.Equal(40000.12m, even.InputTotalTl);
        Assert.Equal(40000.12m, even.Lines[0].RecordedAmountTl);
        Assert.Equal(40.0001m, even.RecordedCostPerKg);
        Assert.Equal(0m, away.RemainderTl);
        Assert.Equal(0m, even.RemainderTl);
    }

    [Fact]
    public void A_direction_not_decided_leaves_the_cost_pending_with_no_figure()
    {
        // URT-D54 (3): whether SAT-D11's AwayFromZero applies was not asked; the calculator picks no direction.
        var result = Round(Cost(), [Label(GoodA, OutputClass.GoodProduct, 900m),
            Label(SecondA, OutputClass.SecondQuality, 50m)], ProductionCostRoundingPolicyInputV1.NotDecided);

        AssertPending(result, ProductionCostRoundingCodesV1.PolicyNotDecided);
        Assert.Equal("cost_rounding_policy_not_decided", result.PendingCode);
    }

    [Fact]
    public void An_unreadable_or_malformed_direction_is_unreadable()
    {
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)];
        ProductionCostRoundingPolicyInputV1[] policies =
        [
            new(ProductionCostRoundingPolicyStatusV1.Unreadable, null),
            new(ProductionCostRoundingPolicyStatusV1.Unreadable, ProductionCostRoundingDirectionV1.HalfToEven),
            new(ProductionCostRoundingPolicyStatusV1.Value, null),
            new(ProductionCostRoundingPolicyStatusV1.Value, (ProductionCostRoundingDirectionV1)99),
            new(ProductionCostRoundingPolicyStatusV1.NotDecided, ProductionCostRoundingDirectionV1.HalfAwayFromZero),
            new((ProductionCostRoundingPolicyStatusV1)99, ProductionCostRoundingDirectionV1.HalfAwayFromZero),
        ];

        foreach (var policy in policies)
        {
            AssertUnreadable(Round(Cost(), labels, policy), ProductionCostRoundingCodesV1.PolicyUnreadable);
        }

        AssertUnreadable(ProductionCostRoundingCalculatorV1.Round(new ProductionCostRoundingInputV1(
                new ProductionScrapCostDistributionInput(Cost(), labels), null!)),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionCostRoundingCalculatorV1.Round(null!), ProductionReceiptCostCodesV1.InputInvalid);
    }

    [Fact]
    public void An_open_order_is_pending_before_the_direction_is_read()
    {
        IReadOnlyList<ProductionScrapCostLabelInput> labels = [Label(GoodA, OutputClass.GoodProduct, 950m)];

        AssertPending(Round(Cost(closed: false), labels, new ProductionCostRoundingPolicyInputV1(
                ProductionCostRoundingPolicyStatusV1.Unreadable, null)),
            ProductionReceiptCostCodesV1.OrderNotClosed);
        AssertPending(Round(Cost(closed: false), labels, ProductionCostRoundingPolicyInputV1.NotDecided),
            ProductionReceiptCostCodesV1.OrderNotClosed);
    }

    [Fact]
    public void A_pending_distribution_stays_pending_and_an_unreadable_direction_wins_over_it()
    {
        // 50 kg weighed scrap with no price entered for its month (URT-D47): pending.
        var cost = Cost(weighing: Weighing(50m, ScrapPriceInput.Missing));
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)];

        AssertPending(Round(cost, labels), ProductionReceiptCostCodesV1.ScrapPriceMissing);
        AssertPending(Round(cost, labels, ProductionCostRoundingPolicyInputV1.NotDecided),
            ProductionReceiptCostCodesV1.ScrapPriceMissing);
        AssertUnreadable(Round(cost, labels, new ProductionCostRoundingPolicyInputV1(
                ProductionCostRoundingPolicyStatusV1.Unreadable, null)),
            ProductionCostRoundingCodesV1.PolicyUnreadable);
    }

    [Fact]
    public void A_zero_issue_cost_is_carried_through_with_the_issue_named_whatever_the_direction()
    {
        // URT-D58 (1): entering the direction would not make the cost readable; the zero-cost issue is named.
        IReadOnlyList<ProductionScrapCostLabelInput> labels =
            [Label(GoodA, OutputClass.GoodProduct, 900m), Label(SecondA, OutputClass.SecondQuality, 50m)];

        foreach (var policy in new[] { AwayFromZero, ProductionCostRoundingPolicyInputV1.NotDecided })
        {
            var result = Round(Cost(issueCost: 0m), labels, policy);

            AssertUnreadable(result, ProductionReceiptCostCodesV1.InputCostZero);
            Assert.Equal([new ProductionReceiptCostIssueReasonV1(IssueA, ProductionReceiptCostCodesV1.InputCostZero)],
                result.IssueReasons);
        }
    }

    [Fact]
    public void The_recorded_scales_are_the_decided_ones()
    {
        Assert.Equal(4, ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale);
        Assert.Equal(2, ProductionCostRoundingCalculatorV1.RecordedAmountScale);
        Assert.Equal(1, ProductionCostRoundingCalculatorV1.ContractVersion);
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
        Assert.Null(result.Direction);
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
