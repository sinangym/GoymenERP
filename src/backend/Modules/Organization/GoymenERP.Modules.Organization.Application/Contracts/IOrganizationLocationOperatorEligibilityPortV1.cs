namespace GoymenERP.Modules.Organization.Application.Contracts;

public enum OrganizationLocationOperatorEligibilityStatusV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Ineligible = 3,
    Eligible = 4,
}

public sealed record OrganizationLocationOperatorEligibilityV1(
    OrganizationLocationOperatorEligibilityStatusV1 Status,
    Guid? PartyId = null);

public interface IOrganizationLocationOperatorEligibilityPortV1
{
    Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(
        Guid companyId,
        string actorId,
        Guid partyId,
        CancellationToken cancellationToken = default);
}
