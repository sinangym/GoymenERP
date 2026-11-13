using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D305 (4) production receipt cost: net input cost over the produced kilograms (scrap in, SAT-D307 (1)-(2)),
/// or net input cost less the weighed scrap at the scrap price over the produced kilograms (scrap out,
/// SAT-D316 (1)); returns at the linked issue's stamped cost (SAT-D317 (2)), closed orders only (SAT-D317 (1)). The
/// declared process loss has value zero (SAT-D316 (2)); consumed kg nothing accounts for is a separate unexplained
/// kg, never process loss (URT-D5). More out than in gives no amount, also inside the closing tolerance (URT-D40,
/// URT-D41 (1)). Sinan's answers of #66 6857496471: the scrap-out divisor is good + second quality kg and its
/// example is the golden example (1); a cost per kg of zero or below is unreadable (2); a scrap price above the
/// raw-material kg cost stops the calculation (3); no weighed scrap and no scrap price entered computes (4); the
/// paint's cost is not loaded and the result is marked (5). Fail-closed on anything unknown (scrap price, zero issue
/// cost, usable remnant, another input not tracked in kg, missing input, overflow). The golden test uses the literal
/// numbers of answer (1); the other expected values are this file's own arithmetic of those rules.
/// </summary>
public sealed class ProductionReceiptCostCalculatorTests
{
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    private static readonly Guid IssueB = Guid.Parse("c0000000-0000-0000-0000-00000000000b");
    private static readonly Guid ReturnA = Guid.Parse("d0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ReturnB = Guid.Parse("d0000000-0000-0000-0000-00000000000b");

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
        NonKgInputUse nonKg = NonKgInputUse.None) =>
        new(closed,
            issues ?? [Issue(IssueA, 1000m, 40m)],
            returns ?? [],
            new Kilograms(good),
            new Kilograms(second),
            new Kilograms(remnant),
            new Kilograms(weighedScrap),
            new Kilograms(processLoss),
            scrap,
            scrapPrice ?? ScrapPriceInput.Missing,
            nonKg);

    private static ScrapPriceInput Price(decimal tlPerKg) => new(ScrapPriceReadStatus.Value, tlPerKg);

    [Fact]
    public void Scrap_in_cost_divides_the_net_input_cost_by_good_and_second_quality_kilograms_unrounded()
    {
        // 1000 kg x 40 = 40000 TL over 950 kg produced; precision is the Inventory calculator's, not rounded here.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input());

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithScrap, result.CostBasis);
        Assert.Null(result.CostExclusion);
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
        // With scrap in cost the scrap price is not read: an unreadable one, or one above the 40 TL raw-material kg
        // cost, changes nothing.
        ScrapPriceInput[] prices = [new(ScrapPriceReadStatus.Unreadable, null), Price(1000m)];

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
            new Kilograms(50m),
            Kilograms.Zero,
            ScrapCostSettingStatus.Off,
            Price(10m),
            NonKgInputUse.None));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(41.578947368421052631578947368m, result.CostPerKg);
        Assert.Equal(500m, result.WeighedScrapExpense);
        Assert.Equal(0m, result.ProcessLossKg);
        Assert.Equal(0m, result.ProcessLossRatio);
        Assert.Equal(0m, result.UnexplainedKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
        Assert.Null(result.CostExclusion);
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
    public void Scrap_out_of_cost_with_weighed_scrap_and_no_readable_scrap_price_is_unreadable()
    {
        // SAT-D316 (6): 50 kg of weighed scrap to value and no price entered, or none readable: no amount.
        ScrapPriceInput[] prices =
        [
            ScrapPriceInput.Missing,
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

        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(new ProductionReceiptCostInput(
            true, [Issue(IssueA, 1000m, 40m)], [], new Kilograms(900m), new Kilograms(50m), Kilograms.Zero,
            new Kilograms(50m), Kilograms.Zero, ScrapCostSettingStatus.Off, null!, NonKgInputUse.None)),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void Scrap_out_of_cost_with_no_weighed_scrap_and_no_scrap_price_entered_is_computed()
    {
        // #66 6857496471 (4): no weighed scrap and no scrap price entered for the period: the cost is computed with a
        // zero expense, also when the order has declared process loss or unexplained kg.
        var balanced = ProductionReceiptCostCalculatorV1.Calculate(Input(
            good: 950m, second: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing));
        var withLoss = ProductionReceiptCostCalculatorV1.Calculate(Input(
            processLoss: 30m, scrap: ScrapCostSettingStatus.Off, scrapPrice: ScrapPriceInput.Missing));

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

        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(new ProductionReceiptCostInput(
            true, [Issue(IssueA, 1000m, 40m)], [], new Kilograms(950m), new Kilograms(50m), Kilograms.Zero,
            Kilograms.Zero, Kilograms.Zero, ScrapCostSettingStatus.Off, null!, NonKgInputUse.None)),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void A_scrap_price_equal_to_the_raw_material_kg_cost_is_computed()
    {
        // #66 6857496471 (3) stops a price above the 40 TL raw-material kg cost, not one equal to it:
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
        // #66 6857496471 (3) ("Hesap dursun, fiyat kontrol edilsin"): 1000 kg x 40 TL, so the raw-material kg cost is
        // 40 TL. Each case is above it, also where the scrap value would stay under the net cost and give a cost.
        (decimal Good, decimal Second, decimal WeighedScrap, decimal TlPerKg)[] cases =
        [
            (900m, 50m, 50m, 40.01m),
            (900m, 50m, 50m, 45m),
            // a price per ton typed as a price per kg on 3 kg: 36000 TL would have left 4.21 TL/kg
            (900m, 50m, 3m, 12000m),
            // 100 kg x 300 TL = 30000 TL would have left 11.11 TL/kg
            (900m, 0m, 100m, 300m),
            // a price given with no weighed scrap is checked too (author reading)
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
    public void The_raw_material_kg_cost_is_the_issued_cost_over_the_issued_kilograms_before_returns()
    {
        // Author reading of "emirde kullanilan hammaddenin kg maliyeti" (#66 6857496471 (3)), to be confirmed:
        // A 600 kg x 40 + B 400 kg x 50 = 44000 TL over 1000 kg issued = 44 TL; 100 kg of B back. A 44 TL price is
        // computed and 44.01 TL stops. (Net cost over consumed kg, 39000 / 900 = 43.33 TL, would stop 44 TL.)
        IssueCostInput[] issues = [Issue(IssueA, 600m, 40m), Issue(IssueB, 400m, 50m)];
        ReturnCostInput[] returns = [new(ReturnB, IssueB, new Kilograms(100m))];

        var equal = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 800m, second: 50m, weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(44m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, equal.Status);
        Assert.Equal(36800m / 850m, equal.CostPerKg);
        Assert.Equal(2200m, equal.WeighedScrapExpense);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues, returns: returns, good: 800m, second: 50m, weighedScrap: 50m,
            scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(44.01m))),
            ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
    }

    [Fact]
    public void Scrap_out_of_cost_with_a_scrap_value_over_the_net_input_cost_is_unreadable()
    {
        // A 500 kg x 10 + B 500 kg x 100 = 55000 TL over 1000 kg issued: the raw-material kg cost is 55 TL, so a
        // 55 TL price passes its check. 490 kg of B back: 6000 TL net over 510 kg consumed; 10 kg good, 500 kg
        // weighed scrap x 55 TL = 27500 TL of scrap against 6000 TL of net input.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 500m, 10m), Issue(IssueB, 500m, 100m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(490m))],
            good: 10m,
            second: 0m,
            weighedScrap: 500m,
            scrap: ScrapCostSettingStatus.Off,
            scrapPrice: Price(55m))),
            ProductionReceiptCostCodesV1.ScrapValueExceedsInputCost);
    }

    [Fact]
    public void Scrap_out_of_cost_with_a_scrap_value_equal_to_the_net_input_cost_is_unreadable_never_a_zero_cost()
    {
        // #66 6857496471 (2): the same order with 390 kg good and 120 kg weighed scrap x 50 TL = 6000 TL, the whole
        // net input cost: a product cost of zero is unreadable, not a value.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 500m, 10m), Issue(IssueB, 500m, 100m)],
            returns: [new ReturnCostInput(ReturnB, IssueB, new Kilograms(490m))],
            good: 390m,
            second: 0m,
            weighedScrap: 120m,
            scrap: ScrapCostSettingStatus.Off,
            scrapPrice: Price(50m))),
            ProductionReceiptCostCodesV1.ProductCostNotPositive);
    }

    [Fact]
    public void A_cost_that_rounds_to_zero_in_decimal_is_unreadable_also_with_scrap_in_cost()
    {
        // #66 6857496471 (2) with scrap in cost too (the test above is scrap out of cost): 0.001 kg x 1E-28 TL is
        // below the smallest decimal and gives a zero cost.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [Issue(IssueA, 0.001m, 0.0000000000000000000000000001m)], good: 0.001m, second: 0m)),
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
    public void Paint_tracked_in_litres_is_not_loaded_and_the_result_is_marked(ScrapCostSettingStatus scrap)
    {
        // #66 6857496471 (5): the paint's cost is not loaded on the product in v1, and the value says so.
        var kgOnly = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m)));
        var withPaint = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m), nonKg: NonKgInputUse.PaintLitres));

        Assert.Equal(ProductionReceiptCostStatus.Value, withPaint.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.NonKgInputCostNotIncluded, withPaint.CostExclusion);
        Assert.Null(withPaint.UnreadableCode);
        Assert.Equal(kgOnly.CostPerKg, withPaint.CostPerKg);
        Assert.Equal(kgOnly.WeighedScrapExpense, withPaint.WeighedScrapExpense);
        Assert.Equal(kgOnly.CostBasis, withPaint.CostBasis);
        Assert.Equal(ProductionReceiptCostStatus.Value, kgOnly.Status);
        Assert.Null(kgOnly.CostExclusion);
    }

    [Theory]
    [InlineData(ScrapCostSettingStatus.On)]
    [InlineData(ScrapCostSettingStatus.Off)]
    public void Another_input_not_tracked_in_kg_keeps_the_result_unreadable(ScrapCostSettingStatus scrap)
    {
        // How an input of a process other than Paint that is not tracked in kg is costed is not decided.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: scrap, scrapPrice: Price(10m), nonKg: NonKgInputUse.Other)),
            ProductionReceiptCostCodesV1.NonKgInputUnpriced);
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

        Assert.Equal(ProductionReceiptCostStatus.Pending, result.Status);
        Assert.Null(result.CostPerKg);
        Assert.Null(result.WeighedScrapExpense);
        Assert.Null(result.ProcessLossKg);
        Assert.Null(result.ProcessLossRatio);
        Assert.Null(result.UnexplainedKg);
        Assert.Null(result.CostBasis);
        Assert.Null(result.CostExclusion);
        Assert.Null(result.UnreadableCode);
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
        // The issue's stamped cost values its return (SAT-D317 (2)) and counts in the raw-material kg cost.
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
    public void A_usable_remnant_keeps_the_result_unreadable_until_its_cost_share_is_decided()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(remnant: 10m)),
            ProductionReceiptCostCodesV1.UsableRemnantUnpriced);
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
    }

    [Fact]
    public void A_missing_input_or_an_undefined_non_kg_input_value_is_invalid_and_does_not_throw()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(null!),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(nonKg: (NonKgInputUse)0)),
            ProductionReceiptCostCodesV1.InputInvalid);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(nonKg: (NonKgInputUse)4)),
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
    public void The_result_does_not_depend_on_the_order_of_issues_and_returns()
    {
        IssueCostInput[] issues = [Issue(IssueA, 600.123m, 41.7777m), Issue(IssueB, 399.877m, 52.3333m)];
        ReturnCostInput[] returns =
        [
            new(ReturnA, IssueA, new Kilograms(12.345m)),
            new(ReturnB, IssueB, new Kilograms(7.5m)),
        ];

        var forward = ProductionReceiptCostCalculatorV1.Calculate(Input(issues: issues, returns: returns));
        var backward = ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: issues.Reverse().ToArray(), returns: returns.Reverse().ToArray()));

        Assert.Equal(ProductionReceiptCostStatus.Value, forward.Status);
        Assert.Equal(forward, backward);
    }

    private static void AssertUnreadable(ProductionReceiptCostResult result, string code)
    {
        Assert.Equal(ProductionReceiptCostStatus.Unreadable, result.Status);
        Assert.Equal(code, result.UnreadableCode);
        Assert.Null(result.CostPerKg);
        Assert.Null(result.WeighedScrapExpense);
        Assert.Null(result.ProcessLossKg);
        Assert.Null(result.ProcessLossRatio);
        Assert.Null(result.UnexplainedKg);
        Assert.Null(result.CostBasis);
        Assert.Null(result.CostExclusion);
    }
}
