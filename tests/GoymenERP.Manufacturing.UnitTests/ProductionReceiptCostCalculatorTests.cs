using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D305 (4) production receipt cost: net input cost over the produced (scrap in) or consumed (scrap out)
/// kilograms, returns at the linked issue's stamped cost, closed orders only, fail-closed on anything unknown.
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
        ScrapCostSettingStatus scrap = ScrapCostSettingStatus.On,
        bool closed = true) =>
        new(closed,
            issues ?? [Issue(IssueA, 1000m, 40m)],
            returns ?? [],
            new Kilograms(good),
            new Kilograms(second),
            new Kilograms(remnant),
            scrap);

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
    public void Scrap_out_of_cost_is_undecided_and_fails_closed()
    {
        AssertUnreadable(ProductionReceiptCostCalculatorV1.Calculate(Input(scrap: ScrapCostSettingStatus.Off)),
            ProductionReceiptCostCodesV1.ScrapPolicyUndecided);
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
        Assert.Null(result.CostBasis);
    }
}
