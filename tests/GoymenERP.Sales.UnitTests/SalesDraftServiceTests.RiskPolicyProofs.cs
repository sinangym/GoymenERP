using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Actual_collateral_policy_evidence_survives_the_HTTP_projection_and_is_withheld_with_prices()
    {
        var world = new World(); var draft = (await world.Service().CreateDraftAsync(Priced(), Metadata("k6-proof"))).Detail;
        var proof = new SalesPartyRiskPolicyProofV1(Company, Guid.NewGuid(), "4", Guid.NewGuid(), "7", null, null,
            "0", "settings.collateral-type:synthetic:7", "Type", new(2026, 11, 18));
        world.PartyRisk = new FakePartyRisk((party, account) => FinanceLike([])(party, account) with
        { Collateral = new(SalesPartyRiskFigureStateV1.Available, "finance.party-collateral", [], null, null, [proof]) });
        var check = await world.Service().GetDraftRiskCheckAsync(draft.Draft.Id);
        Assert.Equal(proof, Assert.Single(check.Collateral.PolicyProofs!));
        world.Authority.Denied.Add(PriceReadPermission);
        Assert.Null((await world.Service().GetDraftRiskCheckAsync(draft.Draft.Id)).Collateral.PolicyProofs);
    }

    [Fact]
    public async Task Foreign_or_contradictory_collateral_policy_evidence_never_becomes_an_available_owner_answer()
    {
        var world = new World(); var draft = (await world.Service().CreateDraftAsync(Priced(), Metadata("k6-invalid-proof"))).Detail;
        var proof = new SalesPartyRiskPolicyProofV1(Company, Guid.NewGuid(), "1", Guid.NewGuid(), "7", null, null,
            "40", "settings.collateral-rate:synthetic:1", "Type", new(2026, 11, 18));
        foreach (var invalid in new[] { proof with { CompanyId = Guid.NewGuid() }, proof with { Scope = "TypeBank" }, proof with { RatePercent = "100.01" } })
        {
            world.PartyRisk = new FakePartyRisk((party, account) => FinanceLike([])(party, account) with
            { Collateral = new(SalesPartyRiskFigureStateV1.Available, "finance.party-collateral", [], null, null, [invalid]) });
            var check = await world.Service().GetDraftRiskCheckAsync(draft.Draft.Id);
            Assert.Equal("Unavailable", check.Collateral.State); Assert.Null(check.Collateral.PolicyProofs);
        }
    }
}
