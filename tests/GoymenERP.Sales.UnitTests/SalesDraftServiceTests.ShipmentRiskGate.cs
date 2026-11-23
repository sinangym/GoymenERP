using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F-018 / plan S2 (FNS-D30, SAT-D310) over SYNTHETIC owners: the shipment risk gate's risk half is the same deterministic
/// evaluation as the risk check and the S1 warning, read as a company-scoped system read — no Sales permission, role or actor is
/// measured, yet every owner read stays fenced on the server company. A missing credit limit is <c>CreditLimitNotSet</c>; a failing
/// owner, an unknown order or another company's order is <c>SourceUnavailable</c>, never "within"; no company throws. The pure
/// mapping and the port's composition are in <see cref="SalesShipmentRiskGateTests"/>.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task F018_S2_the_gate_read_measures_no_sales_permission_writes_nothing_and_maps_a_missing_credit_limit_to_credit_limit_not_set()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var partyRisk = new FakePartyRisk(FinanceLike([]));
        world.PartyRisk = partyRisk;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-gate-limit"))).Detail.Draft;
        var (attempts, envelopes, transitions) = (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count);
        // Every Sales permission is withdrawn: the consumer measured its own, the gate's read measures none.
        world.Authority.Granted = false;
        world.Authority.Measured.Clear();
        var roleReads = world.ActiveRoleCodes.Calls;

        var decision = await world.Service().EvaluateShipmentRiskGateAsync(created.Id);

        Assert.Equal(new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet),
            decision);
        Assert.Equal([(Party, Account)], partyRisk.Asked);
        Assert.Empty(world.Authority.Measured);
        Assert.Equal(roleReads, world.ActiveRoleCodes.Calls);
        // Read-only: no attempt, envelope, transition, refusal or warning.
        Assert.Equal((attempts, envelopes, transitions), (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.RiskWarnings);
    }

    [Fact]
    public async Task F018_S2_a_failing_owner_an_unknown_order_and_another_companys_order_are_source_unavailable_never_within()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var partyRisk = new FakePartyRisk(FinanceLike([]));
        world.PartyRisk = partyRisk;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-gate-source"))).Detail.Draft;

        // The finance owner failing: SourceUnavailable (the S1 rule), never "within".
        partyRisk.Answer = (_, _) => throw new InvalidOperationException("SENTETIK finance outage");
        Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable, await world.Service().EvaluateShipmentRiskGateAsync(created.Id));
        Assert.Single(partyRisk.Asked);

        // An empty or unknown order asks no owner.
        Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable, await world.Service().EvaluateShipmentRiskGateAsync(Guid.Empty));
        Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable, await world.Service().EvaluateShipmentRiskGateAsync(Guid.NewGuid()));
        Assert.Single(partyRisk.Asked);

        // Another company's server context: the order is not its own (the store's tenant filter, and the service checks again).
        var other = new CompanyContext();
        other.Set(OtherRiskCompany);
        world.CompanyOverride = other;
        Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable, await world.Service().EvaluateShipmentRiskGateAsync(created.Id));
        Assert.Single(partyRisk.Asked);
    }

    [Fact]
    public async Task F018_S2_without_a_resolved_company_the_gate_read_throws_and_a_company_change_during_the_read_stays_403()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-gate-company"))).Detail.Draft;

        world.CompanyOverride = new CompanyContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Service().EvaluateShipmentRiskGateAsync(created.Id));

        // The server company changes while the finance owner answers: the next fence refuses, never a SourceUnavailable answer.
        var switching = new SwitchingCompany(Company);
        world.CompanyOverride = switching;
        var answer = FinanceLike([]);
        world.PartyRisk = new FakePartyRisk((party, account) =>
        {
            switching.CompanyId = OtherRiskCompany;
            return answer(party, account);
        });

        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().EvaluateShipmentRiskGateAsync(created.Id));
        Assert.Equal(403, lost.Status);
    }

    /// <summary>A server company context whose company can be changed by a test (the fence must notice it).</summary>
    private sealed class SwitchingCompany(Guid initialCompanyId) : ICompanyContext
    {
        public bool IsResolved => true;

        public Guid CompanyId { get; set; } = initialCompanyId;

        public void Set(Guid companyId) => CompanyId = companyId;
    }
}
