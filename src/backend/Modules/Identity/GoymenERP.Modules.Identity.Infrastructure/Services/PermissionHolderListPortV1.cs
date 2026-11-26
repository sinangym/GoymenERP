using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// WP2 runtime of <see cref="IPermissionHolderListPortV1"/>. Reads only Identity's own tables through its own context; writes
/// nothing. The holder predicate is <c>UserDirectoryReadPortV1</c>'s active-member and holder predicate (SAT-D27), applied per
/// assignment so the scope of each holding is reported.
/// </summary>
internal sealed class PermissionHolderListPortV1(IdentityDbContext dbContext, ICompanyContext companyContext, TimeProvider time)
    : IPermissionHolderListPortV1
{
    public async Task<PermissionHolderListAnswerV1> ListAsync(PermissionHolderListRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || request.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(request.Permission)
            || !string.Equals(request.Permission, request.Permission.Trim(), StringComparison.Ordinal)
            || !ErpPermissions.IsKnown(request.Permission) || !SameCompany(request.CompanyId))
        {
            return new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Denied, []);
        }

        try
        {
            var companyId = request.CompanyId;
            var permission = request.Permission;
            var now = time.GetUtcNow();
            var companyWideOnly = ErpPermissions.RequiresCompanyWideScope(permission);
            var assignments = await dbContext.RoleAssignments.AsNoTracking()
                .Where(assignment => assignment.CompanyId == companyId
                    && assignment.Status == RoleAssignmentStatus.Active
                    && assignment.ValidFromUtc <= now
                    && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                    && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                    && dbContext.RolePermissions.Any(rolePermission =>
                        rolePermission.RoleId == assignment.RoleId && rolePermission.PermissionCode == permission)
                    && dbContext.Users.Any(user => user.Id == assignment.UserId && user.IsActive)
                    && (companyWideOnly
                        ? assignment.Scopes.Any(scope => scope.ScopeType == AccessScopeType.Company && scope.ScopeId == companyId)
                        : assignment.Scopes.Any()))
                .Select(assignment => new
                {
                    assignment.UserId,
                    RoleCode = dbContext.Roles.Where(role => role.Id == assignment.RoleId).Select(role => role.Code).First(),
                    IsGeneralManager = dbContext.RoleAssignments.Any(other => other.CompanyId == companyId && other.UserId == assignment.UserId
                        && other.Status == RoleAssignmentStatus.Active && other.ValidFromUtc <= now
                        && (!other.ValidUntilUtc.HasValue || other.ValidUntilUtc > now)
                        && other.Scopes.Any() && dbContext.Roles.Any(role => role.Id == other.RoleId && role.IsActive
                            && role.Code == ErpPersonaRoleCodes.GeneralManager)),
                    Scopes = assignment.Scopes.Select(scope => new { scope.ScopeType, scope.ScopeId }).ToList(),
                })
                .Take(PermissionHolderListLimitsV1.MaximumHolders * 4 + 1)
                .ToListAsync(cancellationToken);
            if (!SameCompany(companyId))
            {
                return new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Denied, []);
            }

            if (assignments.Count > PermissionHolderListLimitsV1.MaximumHolders * 4)
            {
                return new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Unavailable, []);
            }

            var holders = assignments.GroupBy(assignment => assignment.UserId)
                .Select(group =>
                {
                    var scopes = group.SelectMany(assignment => assignment.Scopes).ToArray();
                    var companyWide = scopes.Any(scope => scope.ScopeType == AccessScopeType.Company && scope.ScopeId == companyId);
                    Guid[] Of(AccessScopeType type) => companyWide
                        ? []
                        : scopes.Where(scope => scope.ScopeType == type).Select(scope => scope.ScopeId).Distinct().Order().ToArray();
                    var managerScopes = group.Where(assignment => ErpPersonaRoleCodes.ModuleManagers.Contains(assignment.RoleCode))
                        .SelectMany(assignment => assignment.Scopes).ToArray();
                    var managerWide = managerScopes.Any(scope => scope.ScopeType == AccessScopeType.Company && scope.ScopeId == companyId);
                    Guid[] ManagerOf(AccessScopeType type) => managerWide ? [] : managerScopes.Where(scope => scope.ScopeType == type)
                        .Select(scope => scope.ScopeId).Distinct().Order().ToArray();
                    return new PermissionHolderV1(group.Key, companyWide, Of(AccessScopeType.Site), Of(AccessScopeType.Location),
                        Of(AccessScopeType.WorkCenter), group.Any(assignment => assignment.IsGeneralManager),
                        managerScopes.Length == 0 ? null : new PermissionHolderScopeV1(managerWide, ManagerOf(AccessScopeType.Site),
                            ManagerOf(AccessScopeType.Location), ManagerOf(AccessScopeType.WorkCenter)));
                })
                .Where(holder => holder.UserId != Guid.Empty)
                .OrderBy(holder => holder.UserId)
                .ToArray();
            return holders.Length > PermissionHolderListLimitsV1.MaximumHolders
                ? new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Unavailable, [])
                : new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Found, holders);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PermissionHolderListAnswerV1(UserDirectoryOutcomeV1.Unavailable, []);
        }
    }

    private bool SameCompany(Guid companyId) => companyContext.IsResolved && companyContext.CompanyId == companyId;
}
