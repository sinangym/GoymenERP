using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class OrganizationLocationAuthorityRevalidationV1(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : IOrganizationLocationAuthorityRevalidationV1
{
    public async Task<OrganizationLocationAuthorityOutcomeV1> RevalidateAsync(
        OrganizationLocationAuthorityRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateSnapshot(request, out var companyId, out var actorId, out var permission, out var locationIds)
            || !MatchesCurrentContext(companyId, actorId))
        {
            return OrganizationLocationAuthorityOutcomeV1.Denied;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            permission,
            companyId,
            cancellationToken);
        if (!scope.IsGranted)
        {
            return OrganizationLocationAuthorityOutcomeV1.Denied;
        }

        var authorizedCount = await OrganizationScopes.ApplyLocationScope(
                dbContext,
                dbContext.Locations.AsNoTracking(),
                scope)
            .CountAsync(location => locationIds.Contains(location.Id), cancellationToken);

        return authorizedCount == locationIds.Length
            ? OrganizationLocationAuthorityOutcomeV1.Authorized
            : OrganizationLocationAuthorityOutcomeV1.Denied;
    }

    private bool MatchesCurrentContext(Guid companyId, string actorId) =>
        companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.IsAuthenticated
        && currentUser.CanAccessCompany(companyId)
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private static bool TryCreateSnapshot(
        OrganizationLocationAuthorityRequestV1? request,
        out Guid companyId,
        out string actorId,
        out string permission,
        out Guid[] locationIds)
    {
        companyId = Guid.Empty;
        actorId = string.Empty;
        permission = string.Empty;
        locationIds = [];

        if (request is null
            || request.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ActorId)
            || !string.Equals(request.ActorId, request.ActorId.Trim(), StringComparison.Ordinal)
            || request.LocationIds is null
            || request.LocationIds.Count is < 1 or > OrganizationLocationAuthorityContractV1.MaximumLocationCount)
        {
            return false;
        }

        permission = request.Access switch
        {
            OrganizationLocationAuthorityAccessV1.Read => ErpPermissions.OrganizationLocationsRead,
            OrganizationLocationAuthorityAccessV1.Write => ErpPermissions.OrganizationLocationsWrite,
            _ => string.Empty,
        };
        if (permission.Length == 0)
        {
            return false;
        }

        locationIds = request.LocationIds.ToArray();
        if (locationIds.Any(id => id == Guid.Empty)
            || locationIds.Distinct().Count() != locationIds.Length)
        {
            locationIds = [];
            return false;
        }

        companyId = request.CompanyId;
        actorId = request.ActorId;
        return true;
    }
}
