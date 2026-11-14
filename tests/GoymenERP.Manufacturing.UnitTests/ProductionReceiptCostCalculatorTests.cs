using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D305 (4) production receipt cost: net input cost less the usable remnant over the produced kilograms (scrap
/// in, SAT-D307 (1)-(2)), or less the remnant and the weighed scrap at its month's scrap price (scrap out,
/// SAT-D316 (1)); returns at the linked issue's stamped cost (SAT-D317 (2)), closed orders only (SAT-D317 (1)). The
/// declared process loss has value zero (SAT-D316 (2)); consumed kg nothing accounts for is a separate unexplained
/// kg, never process loss (URT-D5). More out than in gives no amount, also inside the closing tolerance (URT-D40,
/// URT-D41 (1)). Sinan's answers of #66 6857496471: the scrap-out divisor is good + second quality kg and its
/// example is the golden example (1); a cost per kg of zero or below is unreadable (2); a scrap price above the
/// raw-material kg cost stops the calculation (3); no weighed scrap and no scrap price entered computes (4); the
/// paint's cost is not loaded and the result says so (5). Sinan's answers of #66 6864631364: that raw-material kg
/// cost is the net one, after returns (1); another input not tracked in kg is not loaded either and the result says
/// so (13); a usable remnant returns at the net raw-material kg cost and leaves the divisor (14); each scrap weighing
/// takes its own month's price and a month with no price entered leaves the cost pending (15). Fail-closed on
/// anything unknown (scrap price, zero issue cost, missing input, overflow). The golden tests use the literal numbers
/// of the answers' examples; the other expected values are this file's own arithmetic of those rules.
/// </summary>
public sealed class ProductionReceiptCostCalculatorTests
{
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    private static readonly Guid IssueB = Guid.Parse("c0000000-0000-0000-0000-00000000000b");
    private static readonly Guid ReturnA = Guid.Parse("d0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ReturnB = Guid.Parse("d0000000-0000-0000-0000-00000000000b");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ScrapB = Guid.Parse("e0000000-0000-0000-0000-00000000000b");

    private static IssueCostInput Issue(Guid id, decimal kg, decimal cost) =>
        new(id, new Kilograms(kg), IssueCostReadStatus.Value, cost);

    private static ProductionReceiptCostInput Input(
        IReadOnlyList<IssueCostInput>? issues = null,
        IReadOnlyList<ReturnCostInput>? returns = null,
        decimal good = 900m,
        decimal second = 50m,
        decimal remnant = 0m,
        decimal weighedScrap = 0m,
        decimal processLoss = 0m,
        ScrapCostSettingStatus scrap = ScrapCostSettingStatus.On,
        ScrapPriceInput? scrapPrice = null,
        bool closed = true,
        NonKgInputUse nonKg = NonKgInputUse.None,
        IReadOnlyList<ScrapWeighingInput>? weighings = null) =>
        new(closed,
            issues ?? [Issue(IssueA, 1000m, 40m)],
            returns ?? [],
            new Kilograms(good),
            new Kilograms(second),
            new Kilograms(remnant),
            weighings ?? [Weighing(ScrapA, weighedScrap, scrapPrice ?? ScrapPriceInput.Missing)],
            new Kilograms(processLoss),
            scrap,
            nonKg);

    private static ScrapWeighingInput Weighing(Guid id, decimal kg, ScrapPriceInput price) =>
        new(id, new Kilograms(kg), price);

    private static ScrapPriceInput Price(decimal tlPerKg) => new(ScrapPriceReadStatus.Value, tlPerKg);

    [Fact]
    public void Scrap_in_cost_divides_the_net_input_cost_by_good_and_second_quality_kilograms_unrounded()
    {
        // 1000 kg x 40 = 40000 TL over 950 kg produced; precision is the Inventory calculator's, not rounded here.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input());

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(40m, result.RawMaterialCostPerKg);
        Assert.Equal(0m, result.UsableRemnantValue);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithScrap, result.CostBasis);
        Assert.Null(result.PaintCostNote);
        Assert.Null(result.NonKgInputCostNote);
        Assert.Null(result.PendingCode);
        Assert.Null(result.UnreadableCode);
    }

    [Fact]
    public void Scrap_in_cost_reports_no_separate_scrap_expense_and_the_process_loss_in_kilograms()
    {
        // 1000 kg consumed, 950 kg produced, 30 kg weighed scrap, 20 kg declared process loss: all in the product cost.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 30m, processLoss: 20m));

        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(0m, result.WeighedScrapExpense);
        Assert.Equal(20m, result.ProcessLossKg);
        Assert.Equal(20m / 1000m, result.ProcessLossRatio);
        Assert.Equal(0m, result.UnexplainedKg);
    }

    [Fact]
    public void Scrap_in_cost_needs_no_scrap_price()
    {
        // With scrap in cost the scrap price is not read: one not entered for the weighing's month, an unreadable one,
        // or one above the 40 TL raw-material kg cost changes nothing.
        ScrapPriceInput[] prices = [ScrapPriceInput.Missing, new(ScrapPriceReadStatus.Unreadable, null), Price(1000m)];

        foreach (var price in prices)
        {
            var result = ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 50m, scrapPrice: price));

            Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
            Assert.Equal(40000m / 950m, result.CostPerKg);
            Assert.Equal(0m, result.WeighedScrapExpense);
            Assert.Equal(ProductionReceiptCostCodesV1.BasisWithScrap, result.CostBasis);
        }
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void An_unexplained_difference_is_reported_apart_and_never_becomes_process_loss(ScrapCostSettingStatus scrap)
    {
        // URT-D5 / #66 5917438293 (3A): 1000 kg consumed, 950 kg produced, 30 kg weighed scrap, nothing declared as
        // process loss: the 20 kg are unexplained, not process loss. The cost per kg is the same as when the same
        // 20 kg are declared process loss (both carry value zero and stay on the product).
        var unexplained = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 30m, scrap: scrap, scrapPrice: Price(10m)));
        var declared = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 30m, processLoss: 20m, scrap: scrap, scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, unexplained.Status);
        Assert.Equal(0m, unexplained.ProcessLossKg);
        Assert.Equal(0m, unexplained.ProcessLossRatio);
        Assert.Equal(20m, unexplained.UnexplainedKg);
        Assert.Equal(declared.CostPerKg, unexplained.CostPerKg);
        Assert.Equal(declared.WeighedScrapExpense, unexplained.WeighedScrapExpense);
        Assert.Equal(0m, declared.UnexplainedKg);
    }

    [Fact]
    public void A_partly_declared_process_loss_leaves_only_the_rest_unexplained()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 30m, processLoss: 12.5m));

        Assert.Equal(12.5m, result.ProcessLossKg);
        Assert.Equal(12.5m / 1000m, result.ProcessLossRatio);
        Assert.Equal(7.5m, result.UnexplainedKg);
        Assert.Equal(40000m / 950m, result.CostPerKg);
    }

    [Fact]
    public void Scrap_out_of_cost_golden_example_divides_the_rest_by_good_and_second_quality_kilograms()
    {
        // Golden: Sinan's answer #66 6857496471 (1) ("Saglam + 2. kalite kg"; the option text counts the answer as the
        // golden example). 1000 kg x 40 TL; 900 kg good + 50 kg second quality; 50 kg weighed scrap x 10 TL/kg =
        // 500 TL expense; 39500 / 950 = 41.578947... TL/kg, asserted at the calculator's full decimal precision.
        var result = ProductionReceiptCostCalculatorV1.Calculate(new ProductionReceiptCostInput(
            true,
            [Issue(IssueA, 1000m, 40m)],
            [],
            new Kilograms(900m),
            new Kilograms(50m),
            Kilograms.Zero,
            [Weighing(ScrapA, 50m, Price(10m))],
            Kilograms.Zero,
            ScrapCostSettingStatus.Off,
            NonKgInputUse.None));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(41.578947368421052631578947368m, result.CostPerKg);
        Assert.Equal(40m, result.RawMaterialCostPerKg);
        Assert.Equal(0m, result.UsableRemnantValue);
        Assert.Equal(500m, result.WeighedScrapExpense);
        Assert.Equal(0m, result.ProcessLossKg);
        Assert.Equal(0m, result.ProcessLossRatio);
        Assert.Equal(0m, result.UnexplainedKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
        Assert.Null(result.PaintCostNote);
        Assert.Null(result.NonKgInputCostNote);
        Assert.Null(result.PendingCode);
        Assert.Null(result.UnreadableCode);
    }

    [Fact]
    public void Scrap_out_of_cost_uses_the_net_cost_after_returns()
    {
        // 39000 TL net over 900 kg consumed; 850 kg produced and 50 kg weighed scrap at 12 TL/kg = 600 TL.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 600m, 40m), Issue(IssueB, 400m, 50m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(100m))],
            good: 800m,
            second: 50m,
            weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off,
            scrapPrice: Price(12m)));

        Assert.Equal(38400m / 850m, result.CostPerKg);
        Assert.Equal(600m, result.WeighedScrapExpense);
    }

    [Fact]
    public void Scrap_out_of_cost_values_the_process_loss_at_zero_and_reports_its_kilograms_and_ratio()
    {
        // SAT-D316 (2): 1000 kg consumed, 950 kg produced, 30 kg weighed scrap at 10 TL/kg = 300 TL
        // expense; the 20 kg declared process loss has value zero, so its cost stays on the product: (40000 - 300) / 950.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 30m, processLoss: 20m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(39700m / 950m, result.CostPerKg);
        Assert.Equal(300m, result.WeighedScrapExpense);
        Assert.Equal(20m, result.ProcessLossKg);
        Assert.Equal(20m / 1000m, result.ProcessLossRatio);
        Assert.Equal(0m, result.UnexplainedKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
    }

    [Fact]
    public void Scrap_out_of_cost_with_weighed_scrap_and_an_unreadable_or_malformed_scrap_price_is_unreadable()
    {
        // SAT-D316 (6): 50 kg of weighed scrap to value and no readable price: no amount. A price that was not
        // entered is pending, not unreadable (#66 6864631364 (15), see the pending test below).
        ScrapPriceInput[] prices =
        [
            new(ScrapPriceReadStatus.Unreadable, null),
            new(ScrapPriceReadStatus.Value, null),
            Price(-0.01m),
            new(ScrapPriceReadStatus.Missing, 10m),
        ];

        foreach (var price in prices)
        {
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: price)),
                ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
        }

        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off, weighings: [Weighing(ScrapA, 50m, null!)])),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void Scrap_out_of_cost_with_no_weighed_scrap_and_no_scrap_price_entered_is_computed()
    {
        // #66 6857496471 (4): a weighing of zero kg with no scrap price entered for its month, or no weighing at all:
        // the cost is computed with a zero expense, also when the order has declared process loss or unexplained kg.
        var balanced = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950m, second: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing));
        var withLoss = ProductionReceiptCostCalculatorV1.Calculate(Input(
            processLoss: 30m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing));
        var noWeighing = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950m, second: 50m, scrap: ScrapCostSettingStatus.Off, weighings: []));

        Assert.Equal(ProductionReceiptCostStatus.Value, balanced.Status);
        Assert.Equal(40m, balanced.CostPerKg);
        Assert.Equal(0m, balanced.WeighedScrapExpense);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, balanced.CostBasis);
        Assert.Null(balanced.UnreadableCode);
        Assert.Equal(ProductionReceiptCostStatus.Value, withLoss.Status);
        Assert.Equal(40000m / 950m, withLoss.CostPerKg);
        Assert.Equal(0m, withLoss.WeighedScrapExpense);
        Assert.Equal(30m, withLoss.ProcessLossKg);
        Assert.Equal(20m, withLoss.UnexplainedKg);
        Assert.Equal(balanced, noWeighing);
    }

    [Fact]
    public void Scrap_out_of_cost_with_no_weighed_scrap_still_refuses_an_unreadable_or_malformed_scrap_price()
    {
        // Only a price that was not entered is let through with no weighed scrap; one that could not be read or is
        // malformed stays unreadable (author reading of #66 6857496471 (4), which answers the not-entered case).
        ScrapPriceInput[] prices =
        [
            new(ScrapPriceReadStatus.Unreadable, null),
            new(ScrapPriceReadStatus.Value, null),
            new(ScrapPriceReadStatus.Missing, 10m),
            Price(-0.01m),
        ];

        foreach (var price in prices)
        {
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                good: 950m, second: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: price)),
                ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
        }

        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950m, second: 50m, scrap: ScrapCostSettingStatus.Off, weighings: [Weighing(ScrapA, 0m, null!)])),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void A_scrap_price_equal_to_the_raw_material_kg_cost_is_computed()
    {
        // #66 6857496471 (3) stops a price above the 40 TL net raw-material kg cost, not one equal to it:
        // 50 kg x 40 TL = 2000 TL expense, (40000 - 2000) / 950 TL/kg.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(40m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(38000m / 950m, result.CostPerKg);
        Assert.Equal(2000m, result.WeighedScrapExpense);
    }

    [Fact]
    public void A_scrap_price_above_the_raw_material_kg_cost_stops_the_calculation()
    {
        // #66 6857496471 (3) ("Hesap dursun, fiyat kontrol edilsin"): 1000 kg x 40 TL, so the net raw-material kg cost
        // is 40 TL. Each case is above it, also where the scrap value would stay under the net cost and give a cost.
        (decimal Good, decimal Second, decimal WeighedScrap, decimal TlPerKg)[] cases =
        [
            (900m, 50m, 50m, 40.01m),
            (900m, 50m, 50m, 45m),
            // a price per ton typed as a price per kg on 3 kg: 36000 TL would have left 4.21 TL/kg
            (900m, 50m, 3m, 12000m),
            // 100 kg x 300 TL = 30000 TL would have left 11.11 TL/kg
            (900m, 0m, 100m, 300m),
            // a price given on a weighing of zero kg is checked too (author reading)
            (900m, 50m, 0m, 40.01m),
        ];

        foreach (var item in cases)
        {
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                good: item.Good, second: item.Second, weighedScrap: item.WeighedScrap,
                scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(item.TlPerKg))),
                ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
        }
    }

    [Fact]
    public void Golden_the_raw_material_kg_cost_is_the_net_cost_over_the_consumed_kilograms_after_returns()
    {
        // Golden: Sinan's answer #66 6864631364 (1), "Net", with the literal numbers of its example: 500 kg x 40 TL
        // and 500 kg x 50 TL issued (gross 45 TL/kg), 400 kg of the 50 TL lot back: 25000 TL / 600 kg = 41.67 TL/kg.
        // A 43 TL/kg scrap price passes the gross reading and stops the net one; so does 41.67, which is above
        // 41.666...; 41.66 computes. The 600 kg out are 500 kg good, 50 kg second quality and 50 kg weighed scrap.
        IssueCostInput[] issues = [Issue(IssueA, 500m, 40m), Issue(IssueB, 500m, 50m)];
        ReturnCostInput[] returns = [new(ReturnB, IssueB, new Kilograms(400m))];

        foreach (var price in new[] { 43m, 41.67m })
        {
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                issues: issues, returns: returns, good: 500m, second: 50m, weighedScrap: 50m,
                scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(price))),
                ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
        }

        var computed = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 500m, second: 50m, weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(41.66m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, computed.Status);
        Assert.Equal(25000m / 600m, computed.RawMaterialCostPerKg);
        Assert.Equal(41.67m, decimal.Round(computed.RawMaterialCostPerKg!.Value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(2083m, computed.WeighedScrapExpense);
        Assert.Equal((25000m - 2083m) / 550m, computed.CostPerKg);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void The_net_raw_material_kg_cost_is_reported_with_scrap_in_and_out_of_cost(ScrapCostSettingStatus scrap)
    {
        // #66 6864631364 (1): the same order as the golden test with no weighed scrap; the net raw-material kg cost
        // is reported on every value.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 500m, 40m), Issue(IssueB, 500m, 50m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(400m))],
            good: 550m,
            second: 50m,
            scrap: scrap));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(25000m / 600m, result.RawMaterialCostPerKg);
        Assert.Equal(25000m / 600m, result.CostPerKg);
    }

    [Fact]
    public void A_scrap_price_equal_to_the_net_raw_material_kg_cost_computes_even_where_the_gross_one_is_higher()
    {
        // #66 6864631364 (1): A 500 kg x 40 + B 500 kg x 50, all 500 kg of B back: net 20000 TL / 500 kg = 40 TL/kg
        // (gross 45 TL/kg). 40 TL computes, 40.001 TL stops (the gross reading would let up to 45 TL through).
        // 400 kg good, 50 kg second quality, 50 kg weighed scrap x 40 TL = 2000 TL: (20000 - 2000) / 450 = 40 TL/kg.
        IssueCostInput[] issues = [Issue(IssueA, 500m, 40m), Issue(IssueB, 500m, 50m)];
        ReturnCostInput[] returns = [new(ReturnB, IssueB, new Kilograms(500m))];

        var equal = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 400m, second: 50m, weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(40m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, equal.Status);
        Assert.Equal(40m, equal.RawMaterialCostPerKg);
        Assert.Equal(2000m, equal.WeighedScrapExpense);
        Assert.Equal(40m, equal.CostPerKg);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 400m, second: 50m, weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(40.001m))),
            ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void A_cost_that_rounds_to_zero_in_decimal_is_unreadable_never_a_zero_cost(ScrapCostSettingStatus scrap)
    {
        // #66 6857496471 (2): 0.001 kg x 1E-28 TL is below the smallest decimal and gives a zero cost.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 0.001m, 0.0000000000000000000000000001m)], good: 0.001m, second: 0m,
            scrap: scrap)),
            ProductionReceiptCostCodesV1.ProductCostNotPositive);
    }

    [Fact]
    public void Scrap_out_of_cost_with_a_zero_scrap_price_loads_the_whole_net_cost_on_the_product()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(0m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(0m, result.WeighedScrapExpense);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void Paint_tracked_in_litres_is_not_loaded_and_the_result_says_so(ScrapCostSettingStatus scrap)
    {
        // #66 6857496471 (5): the paint's cost is not loaded on the product in v1, and the value says so with the
        // Turkish text of the resource file.
        var kgOnly = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m)));
        var withPaint = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m), nonKg: NonKgInputUse.PaintLitres));

        Assert.Equal(ProductionReceiptCostStatus.Value, withPaint.Status);
        Assert.Equal(new ProductionReceiptCostNoteV1("paint_cost_not_included", "boya maliyeti dahil değil"),
            withPaint.PaintCostNote);
        Assert.Null(withPaint.NonKgInputCostNote);
        Assert.Null(withPaint.UnreadableCode);
        Assert.Equal(kgOnly with { PaintCostNote = withPaint.PaintCostNote }, withPaint);
        Assert.Equal(ProductionReceiptCostStatus.Value, kgOnly.Status);
        Assert.Null(kgOnly.PaintCostNote);
        Assert.Null(kgOnly.NonKgInputCostNote);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void Another_input_not_tracked_in_kg_is_not_loaded_and_the_result_says_so(ScrapCostSettingStatus scrap)
    {
        // #66 6864631364 (13): like the paint, an input consumed in pieces, litres or metres is not loaded on the
        // product in v1; the cost per kg comes from the kg inputs only and the value says so.
        var kgOnly = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m)));
        var withOther = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m), nonKg: NonKgInputUse.Other));

        Assert.Equal(ProductionReceiptCostStatus.Value, withOther.Status);
        Assert.Equal(
            new ProductionReceiptCostNoteV1("non_kg_input_cost_not_included", "kg dışı girdi maliyeti dahil değil"),
            withOther.NonKgInputCostNote);
        Assert.Null(withOther.PaintCostNote);
        Assert.Null(withOther.UnreadableCode);
        Assert.Equal(kgOnly with { NonKgInputCostNote = withOther.NonKgInputCostNote }, withOther);
    }

    [Fact]
    public void Paint_and_another_input_not_tracked_in_kg_carry_both_notes()
    {
        var kgOnly = ProductionReceiptCostCalculatorV1.Calculate(Input());
        var both = ProductionReceiptCostCalculatorV1.Calculate(Input(nonKg: NonKgInputUse.PaintLitresAndOther));

        Assert.Equal(ProductionReceiptCostStatus.Value, both.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.PaintCostNotIncluded, both.PaintCostNote?.Code);
        Assert.Equal(ProductionReceiptCostTextsV1.PaintCostNotIncluded, both.PaintCostNote?.Text);
        Assert.Equal(ProductionReceiptCostCodesV1.NonKgInputCostNotIncluded, both.NonKgInputCostNote?.Code);
        Assert.Equal(ProductionReceiptCostTextsV1.NonKgInputCostNotIncluded, both.NonKgInputCostNote?.Text);
        Assert.Equal(kgOnly.CostPerKg, both.CostPerKg);
    }

    [Theory]
    [InlineData(NonKgInputUse.PaintLitres)]
    [InlineData(NonKgInputUse.Other)]
    [InlineData(NonKgInputUse.PaintLitresAndOther)]
    public void A_pending_or_unreadable_result_carries_no_note(NonKgInputUse nonKg)
    {
        Assert.Equal(ProductionReceiptCostResult.Pending(ProductionReceiptCostCodesV1.OrderNotClosed),
            ProductionReceiptCostCalculatorV1.Calculate(Input(closed: false, nonKg: nonKg)));
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(good: 1000.001m, nonKg: nonKg)),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void Second_quality_output_alone_carries_the_whole_cost()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(good: 0m, second: 950m, weighedScrap: 50m));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(0m, result.UnexplainedKg);
    }

    [Fact]
    public void Outputs_and_weighed_scrap_over_the_consumed_kilograms_are_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 50.001m)),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 30m, processLoss: 20.001m)),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void Returns_are_valued_at_the_stamped_cost_of_their_own_issue_not_at_an_average()
    {
        // A: 600 kg x 40 = 24000, B: 400 kg x 50 = 20000; 100 kg of B back -> 44000 - 5000 = 39000 TL over 850 kg
        // produced (a return at the 44 TL average would give 39600 TL).
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 600m, 40m), Issue(IssueB, 400m, 50m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(100m))],
            good: 800m,
            second: 50m));

        Assert.Equal(39000m / 850m, result.CostPerKg);
    }

    [Fact]
    public void An_open_order_gives_pending_and_no_amount()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(closed: false));

        AssertPending(result, ProductionReceiptCostCodesV1.OrderNotClosed);
        Assert.Equal("order_not_closed", result.PendingCode);
    }

    [Theory]
    [InlineData(IssueCostReadStatus.Pending)]
    [InlineData(IssueCostReadStatus.Unreadable)]
    public void An_issue_cost_that_is_not_a_value_makes_the_result_unreadable(IssueCostReadStatus status)
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 600m, 40m), new IssueCostInput(IssueB, new Kilograms(400m), status, null)]));

        AssertUnreadable(result, ProductionReceiptCostCodesV1.InputCostUnreadable);
    }

    [Fact]
    public void A_fully_returned_issue_still_needs_a_readable_cost()
    {
        // The issue's stamped cost values its return (SAT-D317 (2)).
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues:
            [
                Issue(IssueA, 600m, 40m),
                new IssueCostInput(IssueB, new Kilograms(400m), IssueCostReadStatus.Unreadable, null),
            ],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(400m))],
            good: 550m,
            second: 50m)),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
    }

    [Fact]
    public void A_value_status_without_a_cost_or_with_a_negative_cost_is_unreadable()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, null)])),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(issues: [Issue(IssueA, 1000m, -1m)])),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void A_zero_issue_cost_is_not_a_cost_and_makes_the_result_unreadable(ScrapCostSettingStatus scrap)
    {
        // Not decided how a zero-cost issue reads (port contract, SAT-D297 alignment): no amount until then, also
        // when only one of the issues is zero and the other would give a positive net cost.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 1000m, 0m)], scrap: scrap, scrapPrice: Price(0m))),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 600m, 40m), Issue(IssueB, 400m, 0m)], scrap: scrap, scrapPrice: Price(0m))),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void More_out_than_in_gives_no_amount_even_inside_the_closing_tolerance(ScrapCostSettingStatus scrap)
    {
        // URT-D40 (1)-(2), URT-D41 (1): 1000 kg consumed, 1005 kg weighed product (+0.5 %, inside the 2 % closing
        // tolerance of URT-D6) and the smallest weighable excess (1000.001 kg) both give no cost.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 955m, second: 50m, scrap: scrap, scrapPrice: Price(10m))),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950.001m, second: 50m, scrap: scrap, scrapPrice: Price(10m))),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m, second: 50m, weighedScrap: 30m, processLoss: 25m, scrap: scrap, scrapPrice: Price(10m))),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void Out_equal_to_in_is_balanced_and_gives_a_cost(ScrapCostSettingStatus scrap)
    {
        // URT-D40 (1) forbids more out than in, not out equal to in: 1000 kg consumed, 950 + 50 kg produced.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950m, second: 50m, scrap: scrap, scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40m, result.CostPerKg);
        Assert.Equal(0m, result.UnexplainedKg);
    }

    [Fact]
    public void An_unreadable_scrap_setting_makes_the_result_unreadable()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(scrap: ScrapCostSettingStatus.Unreadable)),
            ProductionReceiptCostCodesV1.ScrapSettingUnreadable);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(scrap: (ScrapCostSettingStatus)0)),
            ProductionReceiptCostCodesV1.ScrapSettingUnreadable);
    }

    [Fact]
    public void Golden_a_usable_remnant_returns_at_the_net_raw_material_kg_cost_and_leaves_the_divisor()
    {
        // Golden: Sinan's answer #66 6864631364 (14), "Iade gibi stoga donsun", with the literal numbers of its
        // example: 1000 kg x 40 TL; 900 kg good, 50 kg usable remnant, 50 kg weighed scrap x 10 TL/kg. The remnant
        // returns at 50 x 40 = 2000 TL; (40000 - 2000 - 500) / 900 = 41.67 TL/kg.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m,
            second: 0m,
            remnant: 50m,
            weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off,
            scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(37500m / 900m, result.CostPerKg);
        Assert.Equal(41.67m, decimal.Round(result.CostPerKg!.Value, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(40m, result.RawMaterialCostPerKg);
        Assert.Equal(2000m, result.UsableRemnantValue);
        Assert.Equal(500m, result.WeighedScrapExpense);
        Assert.Equal(0m, result.UnexplainedKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
    }

    [Fact]
    public void A_usable_remnant_also_returns_at_the_net_raw_material_kg_cost_with_scrap_in_cost()
    {
        // Sinan's decision #66 6874860782 (SINAN-CEVAP (10), answering the remnant share of URT-D46 (6)): with scrap
        // in cost the remnant returns at the net raw-material kg cost like URT-D46 / #66 6864631364 (14), and the scrap
        // share is borne by the product only. The same order: remnant 2000 TL, (40000 - 2000) / 900 = 42.22 TL per kg.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m, second: 0m, remnant: 50m, weighedScrap: 50m, scrap: ScrapCostSettingStatus.On));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(38000m / 900m, result.CostPerKg);
        Assert.Equal(2000m, result.UsableRemnantValue);
        Assert.Equal(0m, result.WeighedScrapExpense);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithScrap, result.CostBasis);
    }

    [Fact]
    public void A_usable_remnant_is_valued_at_the_net_raw_material_kg_cost_after_returns()
    {
        // #66 6864631364 (1) and (14): the golden order of answer (1), 25000 TL net over 600 kg consumed; 500 kg good
        // and 100 kg remnant. The remnant leaves at 100 x 41.666... TL.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 500m, 40m), Issue(IssueB, 500m, 50m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(400m))],
            good: 500m,
            second: 0m,
            remnant: 100m));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(100m * (25000m / 600m), result.UsableRemnantValue);
        Assert.Equal((25000m - 100m * (25000m / 600m)) / 500m, result.CostPerKg);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void A_usable_remnant_counts_in_the_mass_balance(ScrapCostSettingStatus scrap)
    {
        // URT-D40: 1000 kg consumed; 900 kg good + 50 kg remnant + 50 kg weighed scrap is out equal to in, one gram
        // more of remnant or of scrap is more out than in.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m, second: 0m, remnant: 50.001m, weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m))),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m, second: 0m, remnant: 50m, weighedScrap: 50.001m, scrap: scrap, scrapPrice: Price(10m))),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        var unexplained = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 900m, second: 0m, remnant: 40m, weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m)));
        Assert.Equal(ProductionReceiptCostStatus.Value, unexplained.Status);
        Assert.Equal(10m, unexplained.UnexplainedKg);
    }

    [Fact]
    public void Each_scrap_weighing_is_valued_at_the_scrap_price_of_its_own_month()
    {
        // #66 6864631364 (15), with the weights of its example: 30 kg weighed in October at the October price
        // (10 TL/kg) and 20 kg weighed in November at the November price (12 TL/kg): 300 + 240 = 540 TL;
        // (40000 - 540) / 950. The calculator takes each weighing's month price as given by the caller.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off,
            weighings: [Weighing(ScrapA, 30m, Price(10m)), Weighing(ScrapB, 20m, Price(12m))]));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(540m, result.WeighedScrapExpense);
        Assert.Equal(39460m / 950m, result.CostPerKg);
        Assert.Equal(0m, result.UnexplainedKg);
    }

    [Fact]
    public void Scrap_out_of_cost_with_weighed_scrap_and_no_price_entered_for_its_month_is_pending()
    {
        // #66 6864631364 (15): a weighing whose month has no scrap price entered leaves the cost pending until the
        // price is entered: not computed and never zero, also when another weighing of the order is priced.
        var oneMissing = ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off,
            weighings: [Weighing(ScrapA, 30m, Price(10m)), Weighing(ScrapB, 20m, ScrapPriceInput.Missing)]));
        var onlyOne = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing));

        AssertPending(oneMissing, ProductionReceiptCostCodesV1.ScrapPriceMissing);
        AssertPending(onlyOne, ProductionReceiptCostCodesV1.ScrapPriceMissing);
        Assert.Equal("scrap_price_missing", oneMissing.PendingCode);
    }

    [Fact]
    public void A_weighing_of_zero_kilograms_with_no_price_entered_does_not_hold_the_cost()
    {
        // #66 6857496471 (4) per weighing: no scrap kg in a month with no price entered adds nothing.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off,
            weighings: [Weighing(ScrapA, 50m, Price(10m)), Weighing(ScrapB, 0m, ScrapPriceInput.Missing)]));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(500m, result.WeighedScrapExpense);
        Assert.Equal(39500m / 950m, result.CostPerKg);
    }

    [Fact]
    public void An_unreadable_or_too_high_scrap_price_wins_over_a_missing_one()
    {
        // Entering the missing month's price would not make the order readable, so the answer is unreadable, not
        // pending, whatever order the weighings are given in.
        ScrapWeighingInput missing = Weighing(ScrapA, 20m, ScrapPriceInput.Missing);
        (ScrapWeighingInput Other, string Code)[] cases =
        [
            (Weighing(ScrapB, 30m, new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null)),
                ProductionReceiptCostCodesV1.ScrapPriceUnreadable),
            (Weighing(ScrapB, 30m, Price(40.01m)), ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost),
        ];

        foreach (var item in cases)
        {
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                scrap: ScrapCostSettingStatus.Off, weighings: [missing, item.Other])), item.Code);
            AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
                scrap: ScrapCostSettingStatus.Off, weighings: [item.Other, missing])), item.Code);
        }

        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off,
            weighings:
            [
                Weighing(ScrapA, 20m, new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null)),
                Weighing(ScrapB, 30m, Price(40.01m)),
            ])),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void An_open_order_is_pending_for_the_order_not_for_a_missing_scrap_price()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing, closed: false));

        AssertPending(result, ProductionReceiptCostCodesV1.OrderNotClosed);
    }

    [Fact]
    public void A_duplicate_scrap_weighing_is_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighings: [Weighing(ScrapA, 20m, Price(10m)), Weighing(ScrapA, 20m, Price(10m))])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void A_return_linked_to_an_unknown_issue_is_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            returns: [new ReturnCostInput(ReturnA, IssueB, new Kilograms(1m))])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void Returns_over_what_remains_of_their_issue_are_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            returns:
            [
                new ReturnCostInput(ReturnA, IssueA, new Kilograms(600m)),
                new ReturnCostInput(ReturnB, IssueA, new Kilograms(400.001m)),
            ])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void A_duplicate_issue_or_return_movement_is_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 500m, 40m), Issue(IssueA, 500m, 40m)])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            returns:
            [
                new ReturnCostInput(ReturnA, IssueA, new Kilograms(1m)),
                new ReturnCostInput(ReturnA, IssueA, new Kilograms(1m)),
            ])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void A_movement_id_used_as_both_an_issue_and_a_return_is_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 600m, 40m), Issue(IssueB, 400m, 50m)],
            returns: [new ReturnCostInput(IssueB, IssueA, new Kilograms(1m))])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void Missing_lists_or_elements_are_invalid_and_do_not_throw()
    {
        var valid = Input();
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(valid with { Issues = null! }),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(valid with { Returns = null! }),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(issues: [Issue(IssueA, 1000m, 40m), null!])),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(returns: [null!])),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(valid with { ScrapWeighings = null! }),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrap: ScrapCostSettingStatus.Off, weighings: [Weighing(ScrapA, 10m, Price(10m)), null!])),
            ProductionReceiptCostCodesV1.InputInvalid);
    }

    [Fact]
    public void A_missing_input_or_an_undefined_non_kg_input_value_is_invalid_and_does_not_throw()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(null!),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(nonKg: (NonKgInputUse)0)),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(nonKg: (NonKgInputUse)5)),
            ProductionReceiptCostCodesV1.InputInvalid);
    }

    [Fact]
    public void An_arithmetic_overflow_is_unreadable_and_does_not_throw()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 1000m, decimal.MaxValue)])),
            ProductionReceiptCostCodesV1.ArithmeticOverflow);
    }

    [Fact]
    public void No_issues_no_production_or_more_produced_than_consumed_is_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(issues: [])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(good: 0m, second: 0m)),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(good: 1000m, second: 0.001m)),
            ProductionReceiptCostCodesV1.MassUnbalanced);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            returns: [new ReturnCostInput(ReturnA, IssueA, new Kilograms(1000m))])),
            ProductionReceiptCostCodesV1.MassUnbalanced);
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_of_issues_returns_and_scrap_weighings()
    {
        IssueCostInput[] issues = [Issue(IssueA, 600.123m, 41.7777m), Issue(IssueB, 399.877m, 52.3333m)];
        ReturnCostInput[] returns =
        [
            new(ReturnA, IssueA, new Kilograms(12.345m)),
            new(ReturnB, IssueB, new Kilograms(7.5m)),
        ];

        ScrapWeighingInput[] weighings =
            [Weighing(ScrapA, 12.5m, Price(9.75m)), Weighing(ScrapB, 17.25m, Price(11.5m))];

        var forward = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 900m, second: 25m, remnant: 7.125m,
            scrap: ScrapCostSettingStatus.Off, weighings: weighings));
        var backward = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues.Reverse().ToArray(), returns: returns.Reverse().ToArray(), good: 900m, second: 25m,
            remnant: 7.125m, scrap: ScrapCostSettingStatus.Off, weighings: weighings.Reverse().ToArray()));

        Assert.Equal(ProductionReceiptCostStatus.Value, forward.Status);
        Assert.Equal(forward, backward);
    }

    private static void AssertUnreadable(ProductionReceiptCostResult result, string code)
    {
        Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
        Assert.Equal(code, result.UnreadableCode);
        Assert.Null(result.PendingCode);
        AssertNoAmount(result);
    }

    private static void AssertPending(ProductionReceiptCostResult result, string code)
    {
        Assert.Equal(ProductionReceiptCostStatus.Pending, result.Status);
        Assert.Equal(code, result.PendingCode);
        Assert.Null(result.UnreadableCode);
        AssertNoAmount(result);
    }

    private static void AssertNoAmount(ProductionReceiptCostResult result)
    {
        Assert.Null(result.CostPerKg);
        Assert.Null(result.RawMaterialCostPerKg);
        Assert.Null(result.UsableRemnantValue);
        Assert.Null(result.WeighedScrapExpense);
        Assert.Null(result.ProcessLossKg);
        Assert.Null(result.ProcessLossRatio);
        Assert.Null(result.UnexplainedKg);
        Assert.Null(result.CostBasis);
        Assert.Null(result.PaintCostNote);
        Assert.Null(result.NonKgInputCostNote);
    }
}
