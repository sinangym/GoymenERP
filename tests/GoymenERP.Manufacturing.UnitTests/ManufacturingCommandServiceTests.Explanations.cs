using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the production cost and mass balance explanations. On the URT-D75 (4)
/// golden (1000 kg x 40 TL; 900 kg good, 50 kg remnant, 50 kg scrap x 10 TL, scrap in cost: 37 500 / 900 = 41.6667 TL/kg)
/// the last step is exactly the production cost read's figure and the steps recompose; without the grant nothing is
/// disclosed. The mass balance explanation ends on the mass balance read's difference. Synthetic values, NOT_ENV.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid GoldenIssue = Guid.Parse("b1000000-0000-0000-0000-0000000d75a4");
    private static readonly Guid GoldenGood = Guid.Parse("a1000000-0000-0000-0000-0000000d75a4");
    private static readonly Guid GoldenRemnant = Guid.Parse("a2000000-0000-0000-0000-0000000d75a4");
    private static readonly Guid GoldenScrap = Guid.Parse("a3000000-0000-0000-0000-0000000d75a4");

    private static ProductionScrapCostDistributionInput UrtD75GoldenInput() =>
        new(new ProductionReceiptCostInput(true,
                [new IssueCostInput(GoldenIssue, new Kilograms(1000m), IssueCostReadStatus.Value, 40m,
                    IssueCostCurrencyInput.FunctionalCurrency)], [],
                new Kilograms(900m), Kilograms.Zero, new Kilograms(50m),
                [new ScrapWeighingInput(GoldenScrap, new Kilograms(50m), new ScrapPriceInput(ScrapPriceReadStatus.Value, 10m))],
                Kilograms.Zero, ScrapCostSettingStatus.On, NonKgInputUse.None),
            [new ProductionScrapCostLabelInput(GoldenGood, "GOOD-1", OutputClass.GoodProduct, new Kilograms(900m)),
                new ProductionScrapCostLabelInput(GoldenRemnant, "REMNANT-1", OutputClass.UsableRemnant, new Kilograms(50m))]);

    private static decimal Kg(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal StepValue(IReadOnlyList<ManufacturingTraceStepViewV1> steps, string code) =>
        Kg(Assert.Single(steps, step => step.Code == code).Value);

    [Fact]
    public async Task WP11_golden_URT_D75_the_production_cost_explanation_ends_exactly_on_the_production_cost_read_figure()
    {
        var fixture = new Fixture();
        var order = CostedOrder(fixture);
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(),
            productionCost: new ManufacturingProductionCostPort(new GoldenCostSource(order.Id, UrtD75GoldenInput())));

        var shown = (await service.GetProductionCostAsync(order.Id, CancellationToken.None)).Resource!;
        fixture.Authority.Permissions.Clear();
        var result = await service.ExplainProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal([ErpPermissions.ManufacturingReportsRead], fixture.Authority.Permissions);
        var explanation = result.Resource!;
        Assert.Equal(("available", "41.6667", "41.6667"), (explanation.Status, shown.CostPerKgTl, explanation.DisplayedCostPerKgTl));
        Assert.Equal(shown.CostPerKgTl, explanation.Steps[^1].Value);
        Assert.Equal(ManufacturingExplanationCodesV1.CostPerKgRecorded, explanation.Steps[^1].Code);
        var steps = explanation.Steps;
        // The steps recompose (the test adds, the server never does).
        Assert.Equal(explanation.Issues.Sum(issue => Kg(issue.AmountTl)), StepValue(steps, ManufacturingExplanationCodesV1.NetInputCost));
        Assert.Equal(40000m, StepValue(steps, ManufacturingExplanationCodesV1.NetInputCost));
        Assert.Equal(2000m, StepValue(steps, ManufacturingExplanationCodesV1.UsableRemnantValue));
        Assert.Equal(500m, StepValue(steps, ManufacturingExplanationCodesV1.WeighedScrapValue));
        Assert.Equal(explanation.ScrapWeighings.Sum(item => Kg(item.ValueTl)), StepValue(steps, ManufacturingExplanationCodesV1.WeighedScrapValue));
        Assert.Equal(StepValue(steps, ManufacturingExplanationCodesV1.NetInputCost)
            - StepValue(steps, ManufacturingExplanationCodesV1.UsableRemnantValue)
            - StepValue(steps, ManufacturingExplanationCodesV1.WeighedScrapValue),
            StepValue(steps, ManufacturingExplanationCodesV1.CostNumerator));
        Assert.Equal(900m, StepValue(steps, ManufacturingExplanationCodesV1.ProducedKg));
        Assert.Equal(StepValue(steps, ManufacturingExplanationCodesV1.CostNumerator) / StepValue(steps, ManufacturingExplanationCodesV1.ProducedKg),
            StepValue(steps, ManufacturingExplanationCodesV1.CostPerKgExact));
        Assert.Equal(decimal.Round(StepValue(steps, ManufacturingExplanationCodesV1.CostPerKgExact), 4, MidpointRounding.AwayFromZero),
            StepValue(steps, ManufacturingExplanationCodesV1.CostPerKgRecorded));
        var issue = Assert.Single(explanation.Issues);
        Assert.Equal((GoldenIssue, "1000", "0", "1000", "40", "40000"),
            (issue.IssueMovementId, issue.IssuedKg, issue.ReturnedKg, issue.KeptKg, issue.TlUnitCostPerKg, issue.AmountTl));
        Assert.Equal((GoldenScrap, "50", (string?)"10", "500"), (explanation.ScrapWeighings[0].ScrapOutputId,
            explanation.ScrapWeighings[0].Kilograms, explanation.ScrapWeighings[0].TlPerKg, explanation.ScrapWeighings[0].ValueTl));
        Assert.Contains("URT-D75 (4) golden", explanation.RuleReferences);
        Assert.Equal(shown.CostBasis, explanation.CostBasis);
    }

    [Fact]
    public async Task WP11_without_the_reports_grant_the_production_cost_explanation_discloses_nothing()
    {
        var (fixture, service) = ProductionCostCase(
            new ManufacturingProductionCostPort(new GoldenCostSource(Guid.Empty, UrtD75GoldenInput())));
        var order = CostedOrder(fixture);
        fixture.Authority.Scope = PermissionScope.Denied;

        var denied = await service.ExplainProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (denied.Status, denied.Code));
        Assert.Null(denied.Resource);
    }

    [Fact]
    public async Task WP11_with_no_cost_port_the_production_cost_explanation_is_unavailable_with_its_reason_and_no_step()
    {
        var (fixture, service) = ProductionCostCase(port: null);
        var order = CostedOrder(fixture);

        var explanation = (await service.ExplainProductionCostAsync(order.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", (string?)"cost_source_unavailable", (string?)null),
            (explanation.Status, explanation.ReasonCode, explanation.DisplayedCostPerKgTl));
        Assert.Empty(explanation.Steps);
        Assert.Empty(explanation.Issues);
        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.ExplainProductionCostAsync(Guid.NewGuid(), CancellationToken.None)).Status);
    }

    [Fact]
    public void WP11_the_calculator_trace_is_captured_in_the_same_run_and_never_serialized_into_stored_evidence()
    {
        var result = ProductionCostRoundingCalculatorV1.Round(UrtD75GoldenInput());
        var trace = result.Distribution!.Cost!.Trace!;

        Assert.Equal(result.Distribution.Cost.CostPerKg, trace.CostPerKgTl);
        Assert.Equal(result.Distribution.Cost.NetInputCost, trace.NetInputCostTl);
        Assert.Equal(result.Distribution.Cost.UsableRemnantValue, trace.UsableRemnantValueTl);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Distribution.Cost);
        Assert.DoesNotContain("\"Trace\"", json, StringComparison.Ordinal);
        Assert.Null(ProductionReceiptCostCalculatorV1.Calculate(UrtD75GoldenInput().Cost with { OrderClosed = false }).Trace);
    }

    [Fact]
    public async Task WP11_the_mass_balance_explanation_ends_on_the_mass_balance_read_difference_and_links_the_issue_facts()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issueFact = Fact(order.Version, "10.5");
        var issued = await fixture.Service.IssueInputAsync(order.Id, issueFact, "wp11-issue", Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, issued.Status);
        var shown = Assert.Single((await fixture.Service.GetMassBalanceAsync(order.Id, CancellationToken.None)).Resource!.Rolls);
        fixture.Authority.Permissions.Clear();

        var result = await fixture.Service.ExplainMassBalanceAsync(order.Id, CancellationToken.None);

        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], fixture.Authority.Permissions);
        var explanation = result.Resource!;
        var roll = Assert.Single(explanation.Rolls);
        Assert.Equal((Roll, shown.Difference.DifferenceKg, shown.Difference.DifferenceKg),
            (roll.ParentRollId, roll.DisplayedDifferenceKg, roll.Steps[^1].Value));
        Assert.Equal(shown.Difference.ActualInputKg, Assert.Single(roll.Steps, step => step.Code == ManufacturingExplanationCodesV1.ActualInputKg).Value);
        Assert.Equal(shown.Difference.AccountedKg, Assert.Single(roll.Steps, step => step.Code == ManufacturingExplanationCodesV1.AccountedKg).Value);
        // The test recomposes BAL-003 from the steps: actual input − Σ categories = difference.
        string[] categories = [ManufacturingExplanationCodesV1.GoodProductKg, ManufacturingExplanationCodesV1.SecondQualityKg,
            ManufacturingExplanationCodesV1.UsableRemnantKg, ManufacturingExplanationCodesV1.ScrapKg,
            ManufacturingExplanationCodesV1.ProcessLossKg, ManufacturingExplanationCodesV1.WorkInProgressKg,
            ManufacturingExplanationCodesV1.MeasurementCorrectionKg];
        Assert.Equal(categories.Sum(code => StepValue(roll.Steps, code)), StepValue(roll.Steps, ManufacturingExplanationCodesV1.AccountedKg));
        Assert.Equal(StepValue(roll.Steps, ManufacturingExplanationCodesV1.ActualInputKg) - StepValue(roll.Steps, ManufacturingExplanationCodesV1.AccountedKg),
            StepValue(roll.Steps, ManufacturingExplanationCodesV1.DifferenceKg));
        Assert.Equal([issueFact.FactId], roll.IssueIds);
        Assert.Equal(("GDC-BAL/1 (master v3 §28.9)", MassBalanceExplanationViewV1.CurrentContractVersion),
            (explanation.FormulaVersion, explanation.ContractVersion));
    }

    [Fact]
    public async Task WP11_without_the_order_read_the_mass_balance_explanation_discloses_nothing()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        fixture.Authority.Scope = PermissionScope.Denied;

        var denied = await fixture.Service.ExplainMassBalanceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied), (denied.Status, denied.Code));
        Assert.Null(denied.Resource);
    }

    private sealed class GoldenCostSource(Guid orderId, ProductionScrapCostDistributionInput input)
        : IManufacturingScrapCostDistributionSourceV1
    {
        public Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(Guid companyId, Guid requestedOrderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ManufacturingScrapCostDistributionSourceReadV1(
                ManufacturingScrapCostDistributionSourceStatusV1.Found, companyId,
                orderId == Guid.Empty ? requestedOrderId : orderId, input));
    }
}
