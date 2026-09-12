using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

// Organization owns company liveness; Identity owns explicit global eligibility and
// origin membership. Neither module maps or writes the other's tables.
public sealed class ProductHostAuthorizationService(SqlProductMasterAuthorizationService identity,
    IOrganizationService organization) : IGroupSharedMasterAuthorizationService
{
    public async Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
        GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
    {
        var before = await identity.GetSnapshotAsync(subject, operation, cancellationToken);
        if (!before.IsGranted) return before;
        var companies = await organization.GetAuthorizedCompaniesAsync(cancellationToken);
        if (!companies.Any(company => company.Id == before.OriginCompanyId))
            return GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation);
        var after = await identity.GetSnapshotAsync(subject, operation, cancellationToken);
        return after.IsGranted && after.LocalActorId == before.LocalActorId && after.OriginCompanyId == before.OriginCompanyId
            && after.AuthorityRevision == before.AuthorityRevision
            ? after : GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation);
    }
}
