using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// One authorization view per request: permission answers are cached per (actor, company,
/// permission) so a request reads a stable image, and <see cref="_snapshotRows"/> holds one
/// per-actor row set for capability snapshots.
/// <para>
/// A caller that must not release a result it already read under an authority since taken away
/// asks <see cref="IErpAuthorityRevalidationService"/> instead: that measures the authority now
/// and replaces the cached answer, so the request's view moves forward rather than being
/// bypassed. Both ports are the same scoped instance; see the DI registration.
/// </para>
/// </summary>
internal sealed class ErpAuthorizationService(
    IdentityDbContext dbContext,
    ICurrentUser currentUser,
    IAuthorizationSnapshotReadObserver? snapshotReadObserver = null,
    IPermissionScopeReadObserver? permissionScopeReadObserver = null)
    : IErpAuthorizationService, IErpAuthorityRevalidationService, IActiveRoleCodeReader
{
    private readonly Dictionary<(Guid UserId, Guid CompanyId, string Permission), PermissionScope>
        _requestCache = [];
    private IReadOnlyList<AuthorizationSnapshotProjection>? _snapshotRows;
    private Guid? _snapshotRowsUserId;

    public async Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
        Guid companyId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var requestedPermissions = permissions
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
        if (requestedPermissions.Length == 0
            || requestedPermissions.Any(permission =>
                string.IsNullOrWhiteSpace(permission)
                || !string.Equals(permission, permission.Trim(), StringComparison.Ordinal)
                || !ErpPermissions.IsKnown(permission))
            || requestedPermissions.Distinct(StringComparer.Ordinal).Count()
            != requestedPermissions.Length)
        {
            throw new InvalidOperationException(
                "Authorization snapshot yalnız bilinen, tekil permission'lar içermelidir.");
        }

        if (companyId == Guid.Empty
            || !Guid.TryParse(currentUser.UserId, out var userId))
        {
            return BuildSnapshot(
                Guid.Empty,
                companyId,
                requestedPermissions,
                []);
        }

        var rows = await GetRequestSnapshotRowsAsync(userId, cancellationToken);
        return BuildSnapshot(
            userId,
            companyId,
            requestedPermissions,
            rows);
    }

    private async Task<IReadOnlyList<AuthorizationSnapshotProjection>>
        GetRequestSnapshotRowsAsync(
            Guid userId,
            CancellationToken cancellationToken)
    {
        if (_snapshotRows is not null)
        {
            if (_snapshotRowsUserId != userId)
            {
                throw new InvalidOperationException(
                    "Request-scoped authorization snapshot farklı aktörler arasında kullanılamaz.");
            }

            return _snapshotRows;
        }

        snapshotReadObserver?.ObserveDatabaseRead();
        _snapshotRows = await ReadSnapshotRowsAsync(
            userId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        _snapshotRowsUserId = userId;
        return _snapshotRows;
    }

    public async Task<bool> HasPermissionAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope.IsGranted;
    }

    private async Task<IReadOnlyList<AuthorizationSnapshotProjection>> ReadSnapshotRowsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        async Task<List<AuthorizationSnapshotProjection>> ReadAsync()
        {
            return await (
                    from user in dbContext.Users.AsNoTracking()
                    join assignment in dbContext.RoleAssignments.AsNoTracking()
                        on user.Id equals assignment.UserId
                    join role in dbContext.Roles.AsNoTracking()
                        on assignment.RoleId equals role.Id
                    join permission in dbContext.RolePermissions.AsNoTracking()
                        on role.Id equals permission.RoleId into rolePermissions
                    from rolePermission in rolePermissions.DefaultIfEmpty()
                    join scope in dbContext.RoleAssignmentScopes.AsNoTracking()
                        on new { assignment.CompanyId, RoleAssignmentId = assignment.Id }
                        equals new { scope.CompanyId, scope.RoleAssignmentId }
                    where user.Id == userId
                        && user.IsActive
                        && assignment.Status == RoleAssignmentStatus.Active
                        && assignment.ValidFromUtc <= now
                        && (!assignment.ValidUntilUtc.HasValue
                            || assignment.ValidUntilUtc > now)
                        && role.IsActive
                    select new AuthorizationSnapshotProjection(
                        rolePermission == null ? null : rolePermission.PermissionCode,
                        assignment.CompanyId,
                        assignment.Id,
                        assignment.Version,
                        role.Id,
                        role.Version,
                        role.Code,
                        scope.ScopeType,
                        scope.ScopeId))
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        // This is deliberately one SQL statement. SQL Server installations are guarded at
        // process start by AuthorizationSnapshotDatabaseReadinessService, which requires
        // READ_COMMITTED_SNAPSHOT. That combination gives one statement-consistent,
        // non-blocking authorization image without the read/write deadlocks caused by a
        // Serializable transaction around this projection.
        return await ReadAsync();
    }

    private static AuthorizationSnapshot BuildSnapshot(
        Guid userId,
        Guid companyId,
        IReadOnlyCollection<string> requestedPermissions,
        IReadOnlyCollection<AuthorizationSnapshotProjection> rows)
    {
        var scopes = requestedPermissions.ToDictionary(
            permission => permission,
            permission =>
            {
                var permissionRows = rows
                    .Where(row => row.CompanyId == companyId)
                    .Where(row => string.Equals(
                        row.Permission,
                        permission,
                        StringComparison.Ordinal))
                    .Select(row => new AuthorizationProjection(
                        row.RoleCode,
                        row.ScopeType,
                        row.ScopeId))
                    .Distinct()
                    .ToArray();
                var scope = BuildScope(companyId, permissionRows);
                return ErpPermissions.RequiresCompanyWideScope(permission)
                    && !scope.IsCompanyWide
                        ? PermissionScope.Denied
                        : scope;
            },
            StringComparer.Ordinal);

        var canonical = new StringBuilder();
        AppendCanonical(canonical, userId.ToString("D"));
        AppendCanonical(canonical, companyId.ToString("D"));
        foreach (var permission in requestedPermissions.OrderBy(
                     permission => permission,
                     StringComparer.Ordinal))
        {
            AppendCanonical(canonical, permission);
        }

        foreach (var row in rows
                     .OrderBy(row => row.CompanyId)
                     .ThenBy(row => row.Permission, StringComparer.Ordinal)
                     .ThenBy(row => row.AssignmentId)
                     .ThenBy(row => row.RoleId)
                     .ThenBy(row => row.ScopeType)
                     .ThenBy(row => row.ScopeId))
        {
            AppendCanonical(canonical, row.CompanyId.ToString("D"));
            AppendCanonical(canonical, row.Permission ?? string.Empty);
            AppendCanonical(canonical, row.AssignmentId.ToString("D"));
            AppendCanonical(canonical, row.AssignmentVersion.ToString());
            AppendCanonical(canonical, row.RoleId.ToString("D"));
            AppendCanonical(canonical, row.RoleVersion.ToString());
            AppendCanonical(canonical, row.RoleCode);
            AppendCanonical(canonical, row.ScopeType.ToString());
            AppendCanonical(canonical, row.ScopeId.ToString("D"));
        }

        var revision = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        var allowedCompanyIds = rows
            .Select(row => row.CompanyId)
            .ToHashSet();
        return new AuthorizationSnapshot(
            $"sha256:{revision}",
            allowedCompanyIds,
            scopes);
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        builder.Append(value.Length)
            .Append(':')
            .Append(value);
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

        return await ReadAndCachePermissionScopeAsync(cacheKey, cancellationToken);
    }

    /// <summary>
    /// Measures the authority as it stands now and moves the request's view to that answer, so a
    /// grant found revoked here cannot be resurrected by a later cached read in the same request.
    /// The same rejections as the cached path apply, and they still cost no read.
    /// </summary>
    public async Task<PermissionScope> RevalidatePermissionScopeAsync(
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

        return await ReadAndCachePermissionScopeAsync((userId, companyId, permission), cancellationToken);
    }

    /// <summary>
    /// Reads active role membership without joining RolePermissions. This is deliberately not part
    /// of the permission cache: persona fences need the assignment that stands now, including when
    /// another role supplies the command permission or a persona role has lost one of its grants.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated
            || companyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyId)
            || !Guid.TryParse(currentUser.UserId, out var userId))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var now = DateTimeOffset.UtcNow;
        var roleCodes = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId
                && assignment.CompanyId == companyId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= now
                && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                && assignment.Scopes.Any(scope => scope.CompanyId == companyId)
                && dbContext.Users.Any(user => user.Id == userId && user.IsActive))
            .Join(
                dbContext.Roles.AsNoTracking().Where(role => role.IsActive),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        return roleCodes.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<PermissionScope> ReadAndCachePermissionScopeAsync(
        (Guid UserId, Guid CompanyId, string Permission) cacheKey,
        CancellationToken cancellationToken)
    {
        var (userId, companyId, permission) = cacheKey;
        permissionScopeReadObserver?.ObserveDatabaseRead(permission, companyId);
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

    private sealed record AuthorizationSnapshotProjection(
        string? Permission,
        Guid CompanyId,
        Guid AssignmentId,
        long AssignmentVersion,
        Guid RoleId,
        long RoleVersion,
        string RoleCode,
        AccessScopeType ScopeType,
        Guid ScopeId);
}
