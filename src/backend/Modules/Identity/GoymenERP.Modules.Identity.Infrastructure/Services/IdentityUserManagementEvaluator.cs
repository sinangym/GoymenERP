using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>The KIM-D16/KIM-D19 answer for one actor managing one target user's group-wide account data.</summary>
internal enum IdentityUserManagementDecision
{
    /// <summary>The actor holds company-wide <c>identity.users.manage</c> in every membership company of the target.</summary>
    Allowed = 1,

    /// <summary>The authority is missing in at least one membership company (HTTP <c>403</c>).</summary>
    Forbidden = 2,

    /// <summary>The target has no valid assignment at all (KIM-D19, HTTP <c>409 identity.user.company_assignment_required</c>).</summary>
    CompanyAssignmentRequired = 3,
}

/// <summary>
/// KIM-D16 group-wide user management evaluator (spec ID-2r "reset-password" precondition 2, KS-23). A user's password
/// and account are group-wide, so who may manage them is decided across every company where the target is a member:
/// <list type="bullet">
/// <item>membership: the target's assignments that are valid now (status <c>Active</c>, started, not ended; any scope
/// type). Revoked, expired and not-yet-started assignments do not count;</item>
/// <item>authority: in EACH membership company, one active user holds <c>identity.users.manage</c> through an active,
/// started, unexpired assignment of an active role with the company-wide scope of that company (the same rule as
/// <see cref="ErpAuthorizationService"/>; the permission requires company-wide scope). Site scopes never count.</item>
/// </list>
/// One evaluator answers both the per-actor question (<see cref="EvaluateAsync"/>, the HTTP password-reset fence) and the
/// recovery precondition (<see cref="AnyOtherActiveUserMayManageAsync"/>), so the two can never diverge. Inside a
/// SERIALIZABLE transaction the reads take range locks (RCSI applies only to READ COMMITTED).
/// </summary>
internal sealed class IdentityUserManagementEvaluator(IdentityDbContext dbContext)
{
    internal const string ManagementPermission = ErpPermissions.IdentityUsersManage;

    /// <summary>The companies where the target holds at least one assignment valid at <paramref name="nowUtc"/>.</summary>
    internal async Task<IReadOnlyList<Guid>> GetMembershipCompanyIdsAsync(
        Guid targetUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await dbContext.RoleAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == targetUserId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= nowUtc
                && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc))
            .Select(assignment => assignment.CompanyId)
            .Distinct()
            .OrderBy(companyId => companyId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The companies where <paramref name="actorUserId"/> holds <c>identity.users.manage</c> company-wide at
    /// <paramref name="nowUtc"/> (the same authority rule as <see cref="EvaluateAsync"/>). A target is KIM-D16 manageable by
    /// the actor exactly when its membership companies are a non-empty subset of this set; the managed-user list uses it to
    /// filter in one query instead of evaluating each row.
    /// </summary>
    internal async Task<IReadOnlyList<Guid>> GetCompanyWideManagedCompanyIdsAsync(
        Guid actorUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        await dbContext.RoleAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == actorUserId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= nowUtc
                && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc)
                && dbContext.Users.Any(user => user.Id == assignment.UserId && user.IsActive)
                && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                && dbContext.RolePermissions.Any(rolePermission =>
                    rolePermission.RoleId == assignment.RoleId
                    && rolePermission.PermissionCode == ManagementPermission)
                && assignment.Scopes.Any(scope =>
                    scope.ScopeType == AccessScopeType.Company && scope.ScopeId == assignment.CompanyId))
            .Select(assignment => assignment.CompanyId)
            .Distinct()
            .OrderBy(companyId => companyId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The user ids of the managed-user list (K3-B finding 1; KIM-D16, KIM-D57, KIM-D58) for
    /// <paramref name="actorUserId"/> in <paramref name="contextCompanyId"/>:
    /// <list type="bullet">
    /// <item>members of the context company whose every membership company is one where the actor holds
    /// <c>identity.users.manage</c> company-wide (KIM-D16), and</item>
    /// <item>users without any valid assignment, only when the actor created them (KIM-D57) or the actor is the founder
    /// account named by the bootstrap marker (KIM-D58). Without a marker the founder branch admits nobody.</item>
    /// </list>
    /// </summary>
    internal async Task<IReadOnlyList<Guid>> GetListedUserIdsAsync(
        Guid actorUserId,
        Guid contextCompanyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var managed = await GetCompanyWideManagedCompanyIdsAsync(actorUserId, nowUtc, cancellationToken);
        var founder = await dbContext.BootstrapStates.AsNoTracking()
            .AnyAsync(state => state.FounderUserId == actorUserId, cancellationToken);
        var valid = dbContext.RoleAssignments.AsNoTracking().Where(assignment => assignment.Status == RoleAssignmentStatus.Active
            && assignment.ValidFromUtc <= nowUtc && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc));
        return await dbContext.Users.AsNoTracking()
            .Where(user => (valid.Any(assignment => assignment.UserId == user.Id && assignment.CompanyId == contextCompanyId)
                    && !valid.Any(assignment => assignment.UserId == user.Id && !managed.Contains(assignment.CompanyId)))
                || ((founder || user.CreatedByUserId == actorUserId) && !valid.Any(assignment => assignment.UserId == user.Id)))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>KIM-D16 fence first, then KIM-D19 (a target without membership cannot be managed from the screen).</summary>
    internal async Task<IdentityUserManagementDecision> EvaluateAsync(
        Guid actorUserId,
        Guid targetUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var membership = await GetMembershipCompanyIdsAsync(targetUserId, nowUtc, cancellationToken);
        if (membership.Count == 0)
        {
            return IdentityUserManagementDecision.CompanyAssignmentRequired;
        }

        var holders = await ReadCompanyWideHoldersAsync(membership, excludedUserId: null, nowUtc, cancellationToken);
        return CoversAll(holders, actorUserId, membership.Count)
            ? IdentityUserManagementDecision.Allowed
            : IdentityUserManagementDecision.Forbidden;
    }

    /// <summary>
    /// <see langword="true"/> when a user other than the target is <see cref="IdentityUserManagementDecision.Allowed"/> for
    /// the target in the given membership companies. The caller handles an empty membership itself (spec: that branch
    /// runs before the evaluator).
    /// </summary>
    internal async Task<bool> AnyOtherActiveUserMayManageAsync(
        Guid targetUserId,
        IReadOnlyList<Guid> membershipCompanyIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membershipCompanyIds);
        if (membershipCompanyIds.Count == 0)
        {
            throw new InvalidOperationException("Üyelik şirketi olmayan hedef değerlendiriciye gönderilmez.");
        }

        var holders = await ReadCompanyWideHoldersAsync(membershipCompanyIds, targetUserId, nowUtc, cancellationToken);
        var companyCount = membershipCompanyIds.Distinct().Count();
        return holders.Select(holder => holder.UserId).Distinct().Any(userId => CoversAll(holders, userId, companyCount));
    }

    private static bool CoversAll(IReadOnlyList<Holder> holders, Guid userId, int companyCount) =>
        holders.Where(holder => holder.UserId == userId).Select(holder => holder.CompanyId).Distinct().Count() == companyCount;

    private async Task<IReadOnlyList<Holder>> ReadCompanyWideHoldersAsync(
        IReadOnlyList<Guid> companyIds,
        Guid? excludedUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var companies = companyIds.Distinct().ToList();
        var rows = await dbContext.RoleAssignments.AsNoTracking()
            .Where(assignment => companies.Contains(assignment.CompanyId)
                && (excludedUserId == null || assignment.UserId != excludedUserId)
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= nowUtc
                && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc)
                && dbContext.Users.Any(user => user.Id == assignment.UserId && user.IsActive)
                && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                && dbContext.RolePermissions.Any(rolePermission =>
                    rolePermission.RoleId == assignment.RoleId
                    && rolePermission.PermissionCode == ManagementPermission)
                && assignment.Scopes.Any(scope =>
                    scope.ScopeType == AccessScopeType.Company && scope.ScopeId == assignment.CompanyId))
            .Select(assignment => new { assignment.UserId, assignment.CompanyId })
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows.Select(row => new Holder(row.UserId, row.CompanyId)).ToList();
    }

    private sealed record Holder(Guid UserId, Guid CompanyId);
}
