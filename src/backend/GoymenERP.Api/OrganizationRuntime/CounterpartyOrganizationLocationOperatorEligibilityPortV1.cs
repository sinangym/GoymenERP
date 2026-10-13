using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.OrganizationRuntime;

internal sealed class CounterpartyOrganizationLocationOperatorEligibilityPortV1(
    ICounterpartyLocationOperatorEligibilitySourceV1 source)
    : IOrganizationLocationOperatorEligibilityPortV1
{
    public async Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(
        Guid companyId,
        string actorId,
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        var answer = await source.ReadForLocationOperatorAsync(
            new CounterpartyLocationOperatorEligibilityRequestV1(companyId, actorId, partyId),
            cancellationToken);

        return answer.Outcome switch
        {
            CounterpartyLocationOperatorEligibilityOutcomeV1.Unavailable => Closed(
                OrganizationLocationOperatorEligibilityStatusV1.Unavailable),
            CounterpartyLocationOperatorEligibilityOutcomeV1.Denied => Closed(
                OrganizationLocationOperatorEligibilityStatusV1.Denied),
            CounterpartyLocationOperatorEligibilityOutcomeV1.NotFound => Closed(
                OrganizationLocationOperatorEligibilityStatusV1.NotFound),
            CounterpartyLocationOperatorEligibilityOutcomeV1.Ineligible => Closed(
                OrganizationLocationOperatorEligibilityStatusV1.Ineligible),
            CounterpartyLocationOperatorEligibilityOutcomeV1.Eligible
                when answer.Operator is { } candidate && candidate.PartyId == partyId =>
                new OrganizationLocationOperatorEligibilityV1(
                    OrganizationLocationOperatorEligibilityStatusV1.Eligible,
                    candidate.PartyId),
            CounterpartyLocationOperatorEligibilityOutcomeV1.Eligible => Closed(
                OrganizationLocationOperatorEligibilityStatusV1.Unavailable),
            _ => Closed(OrganizationLocationOperatorEligibilityStatusV1.Unavailable),
        };
    }

    private static OrganizationLocationOperatorEligibilityV1 Closed(
        OrganizationLocationOperatorEligibilityStatusV1 status) => new(status);
}
