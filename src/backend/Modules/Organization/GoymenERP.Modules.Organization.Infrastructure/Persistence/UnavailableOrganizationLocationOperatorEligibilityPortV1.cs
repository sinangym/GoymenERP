using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class UnavailableOrganizationLocationOperatorEligibilityPortV1
    : IOrganizationLocationOperatorEligibilityPortV1
{
    public Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(
        Guid companyId,
        string actorId,
        Guid partyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OrganizationLocationOperatorEligibilityV1(
            OrganizationLocationOperatorEligibilityStatusV1.Unavailable));
}
