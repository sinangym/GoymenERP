using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed class ErpAuthorizationService(
    IdentityDbContext dbContext,
    ICurrentUser currentUser)
    : IErpAuthorizationService
{
    private readonly Dictionary<(Guid UserId, Guid CompanyId, string Permission), PermissionScope>
        _requestCache = [];

    public async Task<bool> HasPermissionAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope.IsGranted;
    }

    public async Task<PermissionScope> GetPermissionScopeAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (!ErpPermissions.IsKnown(permission)
            || companyId == Guid.Empty
            || !Guid.TryParse(currentUser.UserId, out var userId))
        {
            return PermissionScope.Denied;
        }

        var cacheKey = (userId, companyId, permission);
        if (_requestCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var now = DateTimeOffset.UtcNow;
        var authorizationRows = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId
                && assignment.CompanyId == companyId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= now
                && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                && dbContext.RolePermissions.Any(rolePermission =>
                    rolePermission.RoleId == assignment.RoleId
                    && rolePermission.PermissionCode == permission))
            .Join(
                dbContext.Roles.AsNoTracking().Where(role => role.IsActive),
                assignment => assignment.RoleId,
                role => role.Id,
                (assignment, role) => new { Assignment = assignment, RoleCode = role.Code })
            .SelectMany(
                item => item.Assignment.Scopes,
                (item, scope) => new AuthorizationProjection(
                    item.RoleCode,
                    scope.ScopeType,
                    scope.ScopeId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = BuildScope(companyId, authorizationRows);
        if (ErpPermissions.RequiresCompanyWideScope(permission) && !result.IsCompanyWide)
        {
            result = PermissionScope.Denied;
        }

        _requestCache[cacheKey] = result;
        return result;
    }

    private static PermissionScope BuildScope(
        Guid companyId,
        IReadOnlyCollection<AuthorizationProjection> authorizationRows)
    {
        if (authorizationRows.Count == 0)
        {
            return PermissionScope.Denied;
        }

        var companyWide = authorizationRows.Any(x =>
            x.Type == AccessScopeType.Company && x.Id == companyId);
        return new PermissionScope(
            true,
            companyWide,
            authorizationRows.Select(x => x.RoleCode).ToHashSet(StringComparer.Ordinal),
            authorizationRows.Where(x => x.Type == AccessScopeType.Site)
                .Select(x => x.Id)
                .ToHashSet(),
            authorizationRows.Where(x => x.Type == AccessScopeType.Location)
                .Select(x => x.Id)
                .ToHashSet(),
            authorizationRows.Where(x => x.Type == AccessScopeType.WorkCenter)
                .Select(x => x.Id)
                .ToHashSet());
    }

    private sealed record AuthorizationProjection(
        string RoleCode,
        AccessScopeType Type,
        Guid Id);
}
