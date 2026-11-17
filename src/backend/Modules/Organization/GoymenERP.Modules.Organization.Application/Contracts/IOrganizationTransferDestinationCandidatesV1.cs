namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>Actual active internal and external locations for a consuming transfer command, without an Organization grant.</summary>
public interface IOrganizationTransferDestinationCandidatesV1
{
    Task<OrganizationTransferDestinationCandidatesV1> SearchAsync(Guid companyId,
        OrganizationLocationCandidateQueryV1 query, CancellationToken cancellationToken = default);
}

public sealed record OrganizationTransferDestinationCandidatesV1(
    IReadOnlyList<OrganizationTransferDestinationCandidateV1> Items, bool SearchWindowFull);

/// <summary>Custody follows the existing explicit external operator, not a UI guess or site activity cascade.</summary>
public sealed record OrganizationTransferDestinationCandidateV1(Guid LocationId, string LocationCode, string LocationName,
    Guid SiteId, string SiteCode, string SiteName, bool IsExternal, Guid? OperatorPartyId, long LocationVersion);
