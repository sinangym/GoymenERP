using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

// Server configuration after verified human mapping, never an HTTP body or an auto-grant list.
// Missing mapping denies. An isolated synthetic mapping is not Sinan's normal identity.
public sealed record ProductPilotIssuerBinding(Guid LocalUserId, Guid ExternalIdentityId);

// Unregistered until admission. This adapter never infers Product eligibility from any/all
// company assignments. Those rows below establish only fresh membership in the server origin.
public sealed class SqlProductMasterAuthorizationService(IdentityDbContext database,
    ICurrentUser currentUser, ICompanyContext companyContext, ProductPilotIssuerBinding? issuer,
    TimeProvider? clock = null) : IGroupSharedMasterAuthorizationService
{
    public async Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
        GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (subject != GroupSharedMasterKind.Product || operation is not
            (GroupSharedMasterOperation.Read or GroupSharedMasterOperation.DraftCreate
                or GroupSharedMasterOperation.DraftBasicEdit or GroupSharedMasterOperation.Activate
                or GroupSharedMasterOperation.Deactivate or GroupSharedMasterOperation.Reactivate)
            || issuer is null || issuer.LocalUserId == Guid.Empty || issuer.ExternalIdentityId == Guid.Empty
            || !currentUser.IsAuthenticated || !companyContext.IsResolved
            || !Guid.TryParse(currentUser.UserId, out var actorId) || actorId == Guid.Empty)
            return GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation);
        var origin = companyContext.CompanyId;
        if (!currentUser.CanAccessCompany(origin))
            return GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation);
        // Every admitted operation reads ONLY its own grant, by an explicit arm (#329): no default maps one
        // operation onto another's assignment, which would read e.g. an Activate check as a DraftBasicEdit
        // grant (a privilege escalation). An operation without an arm is refused, never widened.
        var productOperation = operation switch
        {
            GroupSharedMasterOperation.Read => ProductMasterOperation.Read,
            GroupSharedMasterOperation.DraftCreate => ProductMasterOperation.DraftCreate,
            GroupSharedMasterOperation.DraftBasicEdit => ProductMasterOperation.DraftBasicEdit,
            GroupSharedMasterOperation.Activate => ProductMasterOperation.Activate,
            GroupSharedMasterOperation.Deactivate => ProductMasterOperation.Deactivate,
            GroupSharedMasterOperation.Reactivate => ProductMasterOperation.Reactivate,
            _ => throw new InvalidOperationException("A Product master operation without its own grant mapping."),
        };
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        // Exactly one fresh statement per invocation. No request cache, tracked entity or
        // reused authorization snapshot. Existing RCSI readiness is still a host requirement.
        var rows = await (
            from grant in database.Set<ProductMasterAssignment>().AsNoTracking()
            join user in database.Users.AsNoTracking() on grant.UserId equals user.Id
            join role in database.Roles.AsNoTracking() on grant.RoleId equals role.Id
            join external in database.ExternalIdentities.AsNoTracking() on grant.IssuerExternalIdentityId equals external.Id
            join decidingUser in database.Users.AsNoTracking() on external.UserId equals decidingUser.Id
            join membership in database.RoleAssignments.AsNoTracking() on user.Id equals membership.UserId
            join memberRole in database.Roles.AsNoTracking() on membership.RoleId equals memberRole.Id
            join scope in database.RoleAssignmentScopes.AsNoTracking()
                on new { membership.CompanyId, RoleAssignmentId = membership.Id }
                equals new { scope.CompanyId, scope.RoleAssignmentId }
            where user.Id == actorId && user.IsActive && grant.IsActive && grant.Operation == productOperation
                && grant.ValidFromUtc <= now && (!grant.ValidUntilUtc.HasValue || grant.ValidUntilUtc > now)
                && role.IsActive && role.IsSystem && role.Code == ProductMasterAssignment.RequiredRoleCode
                && grant.IssuerUserId == issuer.LocalUserId && external.Id == issuer.ExternalIdentityId
                && decidingUser.Id == issuer.LocalUserId && decidingUser.IsActive
                && membership.CompanyId == origin && membership.Status == RoleAssignmentStatus.Active
                && membership.ValidFromUtc <= now && (!membership.ValidUntilUtc.HasValue || membership.ValidUntilUtc > now)
                && memberRole.IsActive
            select new
            {
                GrantId = grant.Id, grant.RowVersion, grant.DecisionId, grant.DecisionReference,
                grant.ValidFromUtc, grant.ValidUntilUtc, UserVersion = user.Version,
                RoleId = role.Id, RoleCode = role.Code, RoleVersion = role.Version,
                IssuerUserId = decidingUser.Id, IssuerVersion = decidingUser.Version,
                ExternalId = external.Id, external.Issuer, external.Subject,
                MembershipId = membership.Id, MembershipVersion = membership.Version,
                MembershipFrom = membership.ValidFromUtc, MembershipUntil = membership.ValidUntilUtc,
                MemberRoleId = memberRole.Id, MemberRoleVersion = memberRole.Version,
                scope.ScopeType, scope.ScopeId,
            }).ToListAsync(cancellationToken);
        if (rows.Count == 0 || rows.Any(row => row.RowVersion.Length != 8)
            || rows.Select(row => row.GrantId).Distinct().Count() != 1
            // SQL role-code collation can be insensitive. Enforce the canonical code as
            // data after the statement too; no role-name comparison is the grant itself.
            || rows.Any(row => row.RoleCode != ProductMasterAssignment.RequiredRoleCode))
            return GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation);
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Actor = actorId, Origin = origin, Operation = operation,
            Rows = rows.OrderBy(row => row.GrantId).ThenBy(row => row.MembershipId)
                .ThenBy(row => row.ScopeType).ThenBy(row => row.ScopeId).ToArray(),
        })));
        return new GroupSharedMasterAuthorizationSnapshot(true, subject, operation, actorId, origin, revision);
    }
}
