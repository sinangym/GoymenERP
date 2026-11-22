using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12): the line margin explanation shows exactly the margin the line-pricing read shows (golden G01
/// preview, golden M08 approved piece line) with the calculator's steps and the input records; without the margin read or the
/// price read nothing of it is disclosed (403) and no owner cost is read. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task WP11_golden_G01_the_margin_explanation_ends_on_the_line_pricing_margin_and_links_the_cost_observation()
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var source = new MarginCostSource { Cost = 7.1234m, CostCurrency = tl };
        var world = new World { MarginCosts = source };
        var request = Priced(unitPrice: "10", discount: null);
        request = request with { CurrencyId = tl, Lines = [request.Lines[0] with { Quantity = "100" }] };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("wp11-g01"))).Detail;
        var shown = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);

        var explanation = await world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, shown.LineId);

        Assert.Equal(shown, explanation.Margin);
        Assert.Equal(("287.66", "28.77"), (explanation.Margin.Amount, explanation.Margin.Percent));
        Assert.Equal(shown.Amount, Step(explanation, SalesLineMarginTraceCodes.MarginAmount).Value);
        Assert.Equal(shown.Percent, Step(explanation, SalesLineMarginTraceCodes.MarginPercent).Value);
        Assert.Equal(Number(Step(explanation, SalesLineMarginTraceCodes.NetSale).Value)
            - Number(Step(explanation, SalesLineMarginTraceCodes.TotalCost).Value),
            Number(Step(explanation, SalesLineMarginTraceCodes.MarginExact).Value));
        Assert.Equal(("Preview", SalesDraftLineMarginExplanationV1.CurrentContractVersion, SalesMarginTraceRulesV1.CalculatorVersion),
            (explanation.Kind, explanation.ContractVersion, explanation.CalculatorVersion));
        Assert.Equal([SalesMarginTraceSourceKindsV1.OrderLine, SalesMarginTraceSourceKindsV1.CostObservation],
            explanation.Sources.Select(item => item.Kind));
        Assert.Equal("{\"syntheticOwnerVersion\":17}", explanation.Sources[1].Detail);
        Assert.Equal([SalesMarginTraceRulesV1.BaseGolden], explanation.RuleReferences);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
    }

    [Fact]
    public async Task WP11_golden_M08_the_approved_piece_line_explanation_links_the_snapshot_the_frozen_rate_and_the_kg_per_unit()
    {
        var source = new MarginCostSource { Cost = 19.9936m, CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira };
        var (world, draftId) = await ApprovedPieceOrderAsync(source, "wp11-m08");
        var shown = Assert.Single((await world.Service().GetDraftLinePricingAsync(draftId)).Margin.Lines!);

        var explanation = await world.Service().ExplainDraftLineMarginAsync(draftId, shown.LineId);

        Assert.Equal(shown, explanation.Margin);
        Assert.Equal(("9885.70", "28.60"), (Step(explanation, SalesLineMarginTraceCodes.MarginAmount).Value,
            Step(explanation, SalesLineMarginTraceCodes.MarginPercent).Value));
        Assert.Equal("34.5678", Step(explanation, SalesLineMarginTraceCodes.SaleRate).Value);
        Assert.Equal("12.345", Step(explanation, SalesLineMarginTraceCodes.KilogramsPerUnit).Value);
        Assert.Equal("Snapshot", explanation.Kind);
        Assert.Equal([SalesMarginTraceSourceKindsV1.OrderLine, SalesMarginTraceSourceKindsV1.CostSnapshot,
                SalesMarginTraceSourceKindsV1.SaleRate, SalesMarginTraceSourceKindsV1.KilogramsPerUnit],
            explanation.Sources.Select(item => item.Kind));
        Assert.Equal([SalesMarginTraceRulesV1.BaseGolden, SalesMarginTraceRulesV1.ForeignSaleGolden, SalesMarginTraceRulesV1.PieceGolden],
            explanation.RuleReferences);
    }

    [Fact]
    public async Task WP11_without_the_margin_read_the_explanation_is_forbidden_and_no_owner_cost_is_read()
    {
        var source = new MarginCostSource { Cost = 7.1234m, CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira };
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("wp11-denied"))).Detail;
        world.Authority.Denied.Add(MarginReadPermission);

        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, created.Lines[0].LineId));

        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task WP11_without_the_price_read_the_explanation_is_forbidden_before_the_margin_is_measured()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("wp11-price"))).Detail;
        world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();

        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, created.Lines[0].LineId));

        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.DoesNotContain(MarginReadPermission, world.Authority.Measured);
    }

    [Fact]
    public async Task WP11_a_margin_read_withdrawn_during_the_owner_cost_read_discloses_nothing_of_the_trace()
    {
        var world = new World();
        var source = new MarginCostSource
        {
            Cost = 7.1234m, CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira,
            OnRead = () => world.Authority.Denied.Add(MarginReadPermission),
        };
        world.MarginCosts = source;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("wp11-revoked"))).Detail;

        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, created.Lines[0].LineId));

        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task WP11_an_unknown_line_is_not_found_and_an_unavailable_margin_has_its_reason_and_no_step()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("wp11-missing"))).Detail;

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, Guid.NewGuid()));
        var unavailable = await world.Service().ExplainDraftLineMarginAsync(created.Draft.Id, created.Lines[0].LineId);

        Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
        Assert.Equal(("Unavailable", "CostSourceMissing", (string?)null),
            (unavailable.Margin.State, unavailable.Margin.UnavailableReason, unavailable.Margin.Amount));
        Assert.Empty(unavailable.Steps);
        Assert.Equal([SalesMarginTraceSourceKindsV1.OrderLine], unavailable.Sources.Select(item => item.Kind));
    }

    private static SalesMarginTraceStepV1 Step(SalesDraftLineMarginExplanationV1 explanation, string code) =>
        Assert.Single(explanation.Steps, step => step.Code == code);

    private static decimal Number(string text) =>
        decimal.Parse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture);
}
