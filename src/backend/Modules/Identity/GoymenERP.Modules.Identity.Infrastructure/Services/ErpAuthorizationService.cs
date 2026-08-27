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
        var scopes = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId
                && assignment.CompanyId == companyId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= now
                && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                && dbContext.RolePermissions.Any(rolePermission =>
                    rolePermission.RoleId == assignment.RoleId
                    && rolePermission.PermissionCode == permission))
            .SelectMany(assignment => assignment.Scopes)
            .Select(scope => new ScopeProjection(scope.ScopeType, scope.ScopeId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = BuildScope(companyId, scopes);
        _requestCache[cacheKey] = result;
        return result;
    }

    private static PermissionScope BuildScope(
        Guid companyId,
        IReadOnlyCollection<ScopeProjection> scopes)
    {
        if (scopes.Count == 0)
        {
            return PermissionScope.Denied;
        }

        var companyWide = scopes.Any(x =>
            x.Type == AccessScopeType.Company && x.Id == companyId);
        return new PermissionScope(
            true,
            companyWide,
            scopes.Where(x => x.Type == AccessScopeType.Site).Select(x => x.Id).ToHashSet(),
            scopes.Where(x => x.Type == AccessScopeType.Location).Select(x => x.Id).ToHashSet(),
            scopes.Where(x => x.Type == AccessScopeType.WorkCenter).Select(x => x.Id).ToHashSet());
    }

    private sealed record ScopeProjection(AccessScopeType Type, Guid Id);
}
