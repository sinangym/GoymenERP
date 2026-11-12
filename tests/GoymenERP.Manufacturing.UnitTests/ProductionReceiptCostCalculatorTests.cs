using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D305 (4) production receipt cost: net input cost over the produced kilograms (scrap in, SAT-D307 (1)-(2)),
/// or net input cost less the weighed scrap at the scrap price over the produced kilograms (scrap out, SAT-D308 and
/// Sinan #66 6826603722); returns at the linked issue's stamped cost, closed orders only, fail-closed on anything
/// unknown (scrap price, process loss value, usable remnant).
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
        ScrapCostSettingStatus scrap = ScrapCostSettingStatus.On,
        ScrapPriceInput? scrapPrice = null,
        bool closed = true) =>
        new(closed,
            issues ?? [Issue(IssueA, 1000m, 40m)],
            returns ?? [],
            new Kilograms(good),
            new Kilograms(second),
            new Kilograms(remnant),
            new Kilograms(weighedScrap),
            scrap,
            scrapPrice ?? ScrapPriceInput.Missing);

    private static ScrapPriceInput Price(decimal tlPerKg) => new(ScrapPriceReadStatus.Value, tlPerKg);

    [Fact]
    public void Scrap_in_cost_divides_the_net_input_cost_by_good_and_second_quality_kilograms_unrounded()
    {
        // 1000 kg x 40 = 40000 TL over 950 kg produced; precision is the Inventory calculator's, not rounded here.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input());

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithScrap, result.CostBasis);
        Assert.Null(result.UnreadableCode);
    }

    [Fact]
    public void Scrap_in_cost_reports_no_separate_scrap_expense_and_the_process_loss_in_kilograms()
    {
        // 1000 kg consumed, 950 kg produced, 30 kg weighed scrap: 20 kg unmeasured loss, all in the product cost.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 30m));

        Assert.Equal(40000m / 950m, result.CostPerKg);
        Assert.Equal(0m, result.WeighedScrapExpense);
        Assert.Equal(20m, result.ProcessLossKg);
        Assert.Equal(20m / 1000m, result.ProcessLossRatio);
    }

    [Fact]
    public void Scrap_in_cost_needs_no_scrap_price()
    {
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            scrapPrice: new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
    }

    [Fact]
    public void Scrap_out_of_cost_values_the_weighed_scrap_at_the_scrap_price_and_loads_the_rest_on_the_product()
    {
        // Golden (SAT-D308, Sinan #66 6826603722 (a)): 1000 kg x 40 = 40000 TL; 900 kg good + 50 kg second quality,
        // 50 kg weighed scrap at 10 TL/kg = 500 TL expense; (40000 - 500) / 950 TL/kg on both outputs; no loss.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(39500m / 950m, result.CostPerKg);
        Assert.Equal(500m, result.WeighedScrapExpense);
        Assert.Equal(0m, result.ProcessLossKg);
        Assert.Equal(0m, result.ProcessLossRatio);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
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
        // Sinan #66 6826763903 (c): 1000 kg consumed, 950 kg produced, 30 kg weighed scrap at 10 TL/kg = 300 TL
        // expense; the 20 kg unmeasured loss has value zero, so its cost stays on the product: (40000 - 300) / 950.
        var result = ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 30m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(10m)));

        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(39700m / 950m, result.CostPerKg);
        Assert.Equal(300m, result.WeighedScrapExpense);
        Assert.Equal(20m, result.ProcessLossKg);
        Assert.Equal(20m / 1000m, result.ProcessLossRatio);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
    }

    [Fact]
    public void Scrap_out_of_cost_without_a_readable_scrap_price_is_unreadable()
    {
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
            new Kilograms(50m), ScrapCostSettingStatus.Off, null!)),
            ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
    }

    [Fact]
    public void Scrap_out_of_cost_with_a_scrap_value_over_the_net_input_cost_is_unreadable()
    {
        // 50 kg x 801 TL = 40050 TL of scrap against 40000 TL of input.
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            weighedScrap: 50m, scrap: ScrapCostSettingStatus.Off, scrapPrice: Price(801m))),
            ProductionReceiptCostCodesV1.ScrapValueExceedsInputCost);
    }

    [Fact]
    public void Outputs_and_weighed_scrap_over_the_consumed_kilograms_are_unbalanced()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(weighedScrap: 50.001m)),
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
        Assert.Null(result.CostBasis);
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
    public void A_value_status_without_a_cost_or_with_a_negative_cost_is_unreadable()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(
            issues: [new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, null)])),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(issues: [Issue(IssueA, 1000m, -1m)])),
            ProductionReceiptCostCodesV1.InputCostUnreadable);
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
        Assert.Null(result.CostBasis);
    }
}
