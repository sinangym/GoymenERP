using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// AT-004 read-only access view. Three rules shape every method:
/// <list type="number">
/// <item>The company is the server-resolved <see cref="ICompanyContext"/> and nothing else: no parameter can widen it,
/// and a group-wide (multi-company) view is not offered, because who may see several companies' assignments at once
/// is not decided. A company administrator therefore sees exactly the assignments of the company they hold the read
/// in.</item>
/// <item>The read is re-authorized here, company-wide, on top of the endpoint policy. Anything less answers as if
/// nothing existed (an empty list or the code-less 404), so a caller cannot use this service to learn that a user
/// or an assignment exists.</item>
/// <item>Only what the view needs leaves the module: display names, never e-mail addresses, issuers or external
/// subjects. Group-wide Product master assignments (PRD-PERM-001) are not company data and are not shown here —
/// no existing read exposes them, and who may see them is not decided.</item>
/// </list>
/// Nothing is written: no audit row, no command record. The platform audits commands and sensitive exports, not
/// reads, and this view follows that rule rather than inventing a read-audit policy.
/// </summary>
internal sealed class IdentityAccessReadService(
    IdentityDbContext dbContext,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService)
    : IIdentityAccessReadService
{
    public async Task<IReadOnlyList<IdentityAccessUserSummary>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await AuthorizedCompanyAsync(cancellationToken);
        if (companyId is null)
        {
            return [];
        }

        var rows = await ReadAssignmentsAsync(companyId.Value, userId: null, roleId: null, cancellationToken);
        return rows
            .GroupBy(row => row.UserId)
            .Select(group => new IdentityAccessUserSummary(
                group.Key,
                group.First().UserDisplayName,
                group.First().UserIsActive,
                group.Count(),
                group.Count(row => row.Validity == IdentityAccessValidity.Effective)))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.UserId)
            .ToArray();
    }

    public async Task<IdentityAccessUserDetail?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await AuthorizedCompanyAsync(cancellationToken);
        if (companyId is null || userId == Guid.Empty)
        {
            return null;
        }

        var rows = await ReadAssignmentsAsync(companyId.Value, userId, roleId: null, cancellationToken);
        if (rows.Count == 0)
        {
            // An unknown user and a user of another company are the same answer: nothing to show here.
            return null;
        }

        var effectiveRows = rows.Where(row => row.Validity == IdentityAccessValidity.Effective).ToArray();
        var effectiveRoleIds = effectiveRows.Select(row => row.RoleId).Distinct().ToArray();
        var rolePermissions = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(permission => effectiveRoleIds.Contains(permission.RoleId))
            .Select(permission => new { permission.RoleId, permission.PermissionCode })
            .ToListAsync(cancellationToken);
        var roleCodeById = effectiveRows
            .GroupBy(row => row.RoleId)
            .ToDictionary(group => group.Key, group => group.First().RoleCode);
        var effectivePermissions = rolePermissions
            .GroupBy(permission => permission.PermissionCode, StringComparer.Ordinal)
            .Select(group => new IdentityAccessEffectivePermission(
                group.Key,
                ErpPermissions.IsCritical(group.Key),
                group.Select(permission => roleCodeById[permission.RoleId])
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(permission => permission.Code, StringComparer.Ordinal)
            .ToArray();

        var first = rows[0];
        return new IdentityAccessUserDetail(
            first.UserId,
            first.UserDisplayName,
            first.UserIsActive,
            rows.Select(ToAssignment).ToArray(),
            effectivePermissions);
    }

    public async Task<IReadOnlyList<IdentityAccessRoleSummary>> GetRolesAsync(
        CancellationToken cancellationToken = default)
    {
        var companyId = await AuthorizedCompanyAsync(cancellationToken);
        if (companyId is null)
        {
            return [];
        }

        var roles = await dbContext.Roles
            .AsNoTracking()
            .Select(role => new
            {
                role.Id,
                role.Code,
                role.Name,
                role.IsSystem,
                role.IsActive,
                PermissionCount = dbContext.RolePermissions.Count(permission => permission.RoleId == role.Id),
            })
            .ToListAsync(cancellationToken);
        var rows = await ReadAssignmentsAsync(companyId.Value, userId: null, roleId: null, cancellationToken);
        var holders = rows
            .GroupBy(row => row.RoleId)
            .ToDictionary(
                group => group.Key,
                group => (
                    All: group.Select(row => row.UserId).Distinct().Count(),
                    Effective: group.Where(row => row.Validity == IdentityAccessValidity.Effective)
                        .Select(row => row.UserId)
                        .Distinct()
                        .Count()));

        return roles
            .Select(role =>
            {
                var counts = holders.GetValueOrDefault(role.Id);
                return new IdentityAccessRoleSummary(
                    role.Id,
                    role.Code,
                    role.Name,
                    role.IsSystem,
                    role.IsActive,
                    role.PermissionCount,
                    counts.All,
                    counts.Effective);
            })
            .OrderBy(role => role.Code, StringComparer.Ordinal)
            .ThenBy(role => role.RoleId)
            .ToArray();
    }

    public async Task<IdentityAccessRoleDetail?> GetRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await AuthorizedCompanyAsync(cancellationToken);
        if (companyId is null || roleId == Guid.Empty)
        {
            return null;
        }

        var role = await dbContext.Roles
            .AsNoTracking()
            .Where(candidate => candidate.Id == roleId)
            .Select(candidate => new { candidate.Id, candidate.Code, candidate.Name, candidate.IsSystem, candidate.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (role is null)
        {
            return null;
        }

        var permissions = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(permission => permission.RoleId == roleId)
            .Select(permission => permission.PermissionCode)
            .ToListAsync(cancellationToken);
        var rows = await ReadAssignmentsAsync(companyId.Value, userId: null, roleId, cancellationToken);

        return new IdentityAccessRoleDetail(
            role.Id,
            role.Code,
            role.Name,
            role.IsSystem,
            role.IsActive,
            permissions
                .Order(StringComparer.Ordinal)
                .Select(code => new IdentityAccessPermission(code, ErpPermissions.IsCritical(code)))
                .ToArray(),
            rows
                .OrderBy(row => row.UserDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.UserId)
                .ThenBy(row => row.ValidFromUtc)
                .ThenBy(row => row.AssignmentId)
                .Select(row => new IdentityAccessRoleHolder(
                    row.AssignmentId,
                    row.UserId,
                    row.UserDisplayName,
                    row.UserIsActive,
                    row.Status,
                    row.Validity,
                    row.ValidFromUtc,
                    row.ValidUntilUtc,
                    row.ApprovedBy,
                    row.Scopes))
                .ToArray());
    }

    /// <summary>
    /// The context company, only when the caller holds the read company-wide there. A narrower scope is refused
    /// here even before the central catalogue marks the name company-wide only.
    /// </summary>
    private async Task<Guid?> AuthorizedCompanyAsync(CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return null;
        }

        var companyId = companyContext.CompanyId;
        var scope = await authorizationService.GetPermissionScopeAsync(
            IdentityPermissionManifestV1.AssignmentsRead,
            companyId,
            cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide ? companyId : null;
    }

    private async Task<IReadOnlyList<AssignmentRow>> ReadAssignmentsAsync(
        Guid companyId,
        Guid? userId,
        Guid? roleId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.RoleAssignments
            .AsNoTracking()
            .Include(assignment => assignment.Scopes)
            .Where(assignment => assignment.CompanyId == companyId);
        if (userId is not null)
        {
            query = query.Where(assignment => assignment.UserId == userId.Value);
        }

        if (roleId is not null)
        {
            query = query.Where(assignment => assignment.RoleId == roleId.Value);
        }

        var assignments = await query.ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return [];
        }

        var roleIds = assignments.Select(assignment => assignment.RoleId).Distinct().ToArray();
        var roles = await dbContext.Roles
            .AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .Select(role => new { role.Id, role.Code, role.Name, role.IsActive })
            .ToDictionaryAsync(role => role.Id, cancellationToken);
        var userIds = assignments
            .Select(assignment => assignment.UserId)
            .Concat(assignments
                .Where(assignment => assignment.ApprovedByUserId.HasValue)
                .Select(assignment => assignment.ApprovedByUserId!.Value))
            .Distinct()
            .ToArray();
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName, user.IsActive })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        return assignments
            .Where(assignment => roles.ContainsKey(assignment.RoleId) && users.ContainsKey(assignment.UserId))
            .Select(assignment =>
            {
                var role = roles[assignment.RoleId];
                var user = users[assignment.UserId];
                var approver = assignment.ApprovedByUserId is { } approverId
                    && users.TryGetValue(approverId, out var approverUser)
                        ? new IdentityAccessPerson(approverUser.Id, approverUser.DisplayName)
                        : null;
                return new AssignmentRow(
                    assignment.Id,
                    assignment.UserId,
                    user.DisplayName,
                    user.IsActive,
                    role.Id,
                    role.Code,
                    role.Name,
                    role.IsActive,
                    assignment.Status.ToString(),
                    Classify(assignment, role.IsActive, user.IsActive, now),
                    assignment.ValidFromUtc,
                    assignment.ValidUntilUtc,
                    assignment.CreatedAtUtc,
                    approver,
                    assignment.Scopes
                        .OrderBy(scope => scope.ScopeType)
                        .ThenBy(scope => scope.ScopeId)
                        .Select(scope => new IdentityAccessScope(scope.ScopeType.ToString(), scope.ScopeId))
                        .ToArray());
            })
            .OrderBy(row => row.RoleCode, StringComparer.Ordinal)
            .ThenBy(row => row.ValidFromUtc)
            .ThenBy(row => row.AssignmentId)
            .ToArray();
    }

    /// <summary>
    /// The same predicate the authorization service applies (active user, active assignment inside its validity,
    /// active role, at least one scope), classified so the screen can say WHY an assignment does not count.
    /// </summary>
    internal static string Classify(
        RoleAssignment assignment,
        bool roleIsActive,
        bool userIsActive,
        DateTimeOffset now)
    {
        if (assignment.Status == RoleAssignmentStatus.Revoked)
        {
            return IdentityAccessValidity.Revoked;
        }

        if (assignment.Status == RoleAssignmentStatus.Expired
            || (assignment.ValidUntilUtc.HasValue && assignment.ValidUntilUtc.Value <= now))
        {
            return IdentityAccessValidity.Ended;
        }

        if (assignment.ValidFromUtc > now)
        {
            return IdentityAccessValidity.Scheduled;
        }

        return roleIsActive && userIsActive && assignment.Scopes.Count > 0
            ? IdentityAccessValidity.Effective
            : IdentityAccessValidity.Inactive;
    }

    private static IdentityAccessAssignment ToAssignment(AssignmentRow row) => new IdentityAccessAssignment(
        row.AssignmentId,
        row.RoleId,
        row.RoleCode,
        row.RoleName,
        row.RoleIsActive,
        row.Status,
        row.Validity,
        row.ValidFromUtc,
        row.ValidUntilUtc,
        row.CreatedAtUtc,
        row.ApprovedBy,
        row.Scopes);

    private sealed record AssignmentRow(
        Guid AssignmentId,
        Guid UserId,
        string UserDisplayName,
        bool UserIsActive,
        Guid RoleId,
        string RoleCode,
        string RoleName,
        bool RoleIsActive,
        string Status,
        string Validity,
        DateTimeOffset ValidFromUtc,
        DateTimeOffset? ValidUntilUtc,
        DateTimeOffset CreatedAtUtc,
        IdentityAccessPerson? ApprovedBy,
        IReadOnlyList<IdentityAccessScope> Scopes);
}
