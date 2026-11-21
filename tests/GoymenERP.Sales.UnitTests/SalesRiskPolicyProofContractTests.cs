using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesRiskPolicyProofContractTests
{
    [Fact]
    public void Legacy_risk_figures_omit_the_new_nullable_proof_member_and_new_evidence_round_trips_exactly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, "finance.party-collateral", [], null, null);
        Assert.DoesNotContain("policyProofs", JsonSerializer.Serialize(legacy, options));
        var proof = new SalesPartyRiskPolicyProofV1(Guid.NewGuid(), Guid.NewGuid(), "4", Guid.NewGuid(), "7", null, null,
            "0", "settings.collateral-type:synthetic:7", "Type", new(2026, 11, 18));
        var current = legacy with { PolicyProofs = [proof] };
        var wire = JsonSerializer.Serialize(current, options);
        var reread = JsonSerializer.Deserialize<SalesPartyRiskFigureV1>(wire, options)!;
        Assert.Equal(proof, Assert.Single(reread.PolicyProofs!));
        var http = new SalesDraftRiskFigureV1("Collateral", legacy.Source, "Available", null, [], null, [proof]);
        Assert.Equal(proof, Assert.Single(JsonSerializer.Deserialize<SalesDraftRiskFigureV1>(JsonSerializer.Serialize(http, options), options)!.PolicyProofs!));
        Assert.DoesNotContain("policyProofs", JsonSerializer.Serialize(http with { PolicyProofs = null }, options));
    }
}
