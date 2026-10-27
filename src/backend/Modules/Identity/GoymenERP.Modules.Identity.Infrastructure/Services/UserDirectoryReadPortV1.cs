using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// MOD-08 runtime of <see cref="IUserDirectoryReadPortV1"/>. Reads only Identity's own tables through its own context;
/// writes nothing (no audit row: the platform audits commands and sensitive exports, not reads).
/// </summary>
internal sealed class UserDirectoryReadPortV1(
    IdentityDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser)
    : IUserDirectoryReadPortV1
{
    public async Task<UserDirectoryResolveAnswerV1> ResolveAsync(
        UserDirectoryResolveRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadResolve(request, out var userIds) || !TryFence(request?.CompanyId, request?.ActorId, out var fence))
        {
            return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Denied, []);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!await IsActiveMemberAsync(fence.ActorUserId, fence.CompanyId, now, cancellationToken))
            {
                return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Denied, []);
            }

            // Every assignment of the asked users in this company, any state: membership at any time keeps a name.
            var memberships = await dbContext.RoleAssignments
                .AsNoTracking()
                .Where(assignment => assignment.CompanyId == fence.CompanyId && userIds.Contains(assignment.UserId))
                .Select(assignment => new
                {
                    assignment.UserId,
                    Effective = assignment.Status == RoleAssignmentStatus.Active
                        && assignment.ValidFromUtc <= now
                        && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                        && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                        && assignment.Scopes.Any(),
                })
                .ToListAsync(cancellationToken);
            if (memberships.Count == 0)
            {
                return StillFenced(fence)
                    ? new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Found, [])
                    : new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Denied, []);
            }

            var memberIds = memberships.Select(membership => membership.UserId).Distinct().ToArray();
            var effectiveIds = memberships
                .Where(membership => membership.Effective)
                .Select(membership => membership.UserId)
                .ToHashSet();
            var users = await dbContext.Users
                .AsNoTracking()
                .Where(user => memberIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName, user.IsActive })
                .ToListAsync(cancellationToken);

            if (!StillFenced(fence))
            {
                return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Denied, []);
            }

            var items = new List<UserDirectoryEntryV1>(users.Count);
            foreach (var user in users.OrderBy(user => user.Id))
            {
                if (user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.DisplayName))
                {
                    return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Unavailable, []);
                }

                items.Add(new UserDirectoryEntryV1(
                    user.Id,
                    user.DisplayName,
                    user.IsActive && effectiveIds.Contains(user.Id)));
            }

            return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Found, items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new UserDirectoryResolveAnswerV1(UserDirectoryOutcomeV1.Unavailable, []);
        }
    }

    public async Task<UserDirectorySearchAnswerV1> SearchActiveAsync(
        UserDirectorySearchRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadSearch(request, out var query, out var limit, out var holdingPermission)
            || !TryFence(request?.CompanyId, request?.ActorId, out var fence))
        {
            return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Denied, [], false);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!await IsActiveMemberAsync(fence.ActorUserId, fence.CompanyId, now, cancellationToken))
            {
                return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Denied, [], false);
            }

            var users = ActiveMembers(fence.CompanyId, now);
            if (holdingPermission is not null)
            {
                users = HoldersOf(users, holdingPermission, fence.CompanyId, now);
            }

            if (query is not null)
            {
                users = users.Where(user => user.DisplayName.Contains(query));
            }

            var page = await users
                .OrderBy(user => user.DisplayName)
                .ThenBy(user => user.Id)
                .Select(user => new { user.Id, user.DisplayName })
                .Take(limit + 1)
                .ToListAsync(cancellationToken);

            if (!StillFenced(fence))
            {
                return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Denied, [], false);
            }

            var items = new List<UserDirectoryEntryV1>(Math.Min(page.Count, limit));
            foreach (var user in page.Take(limit))
            {
                if (user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.DisplayName))
                {
                    return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Unavailable, [], false);
                }

                items.Add(new UserDirectoryEntryV1(user.Id, user.DisplayName, IsActiveInCompany: true));
            }

            return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Found, items, page.Count > limit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new UserDirectorySearchAnswerV1(UserDirectoryOutcomeV1.Unavailable, [], false);
        }
    }

    /// <summary>
    /// Active users holding an effective assignment in the company now: the same predicate the external identity
    /// resolver uses to grant company access.
    /// </summary>
    private IQueryable<UserAccount> ActiveMembers(Guid companyId, DateTimeOffset now) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive
                && dbContext.RoleAssignments.Any(assignment =>
                    assignment.UserId == user.Id
                    && assignment.CompanyId == companyId
                    && assignment.Status == RoleAssignmentStatus.Active
                    && assignment.ValidFromUtc <= now
                    && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
                    && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
                    && assignment.Scopes.Any()));

    /// <summary>
    /// SAT-D27: narrows the members to those who hold <paramref name="permission"/> in the company now — the authorization
    /// service's measurement (<c>ErpAuthorizationService</c>) as one SQL predicate: an active assignment inside its validity in
    /// the company, of an active role carrying the permission, with a scope in the company; a company-wide-only permission
    /// needs the assignment's company scope.
    /// </summary>
    private IQueryable<UserAccount> HoldersOf(IQueryable<UserAccount> users, string permission, Guid companyId, DateTimeOffset now)
    {
        var companyWideOnly = ErpPermissions.RequiresCompanyWideScope(permission);
        return users.Where(user => dbContext.RoleAssignments.Any(assignment =>
            assignment.UserId == user.Id
            && assignment.CompanyId == companyId
            && assignment.Status == RoleAssignmentStatus.Active
            && assignment.ValidFromUtc <= now
            && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
            && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive)
            && dbContext.RolePermissions.Any(rolePermission =>
                rolePermission.RoleId == assignment.RoleId && rolePermission.PermissionCode == permission)
            && (companyWideOnly
                ? assignment.Scopes.Any(scope => scope.ScopeType == AccessScopeType.Company && scope.ScopeId == companyId)
                : assignment.Scopes.Any())));
    }

    private Task<bool> IsActiveMemberAsync(
        Guid userId,
        Guid companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ActiveMembers(companyId, now).AnyAsync(user => user.Id == userId, cancellationToken);

    /// <summary>The request names exactly the server-resolved company and the authenticated caller, who may access it.</summary>
    private bool TryFence(Guid? companyId, string? actorId, out Fence fence)
    {
        fence = default;
        if (companyId is not { } requestedCompany
            || requestedCompany == Guid.Empty
            || string.IsNullOrWhiteSpace(actorId)
            || !currentUser.IsAuthenticated
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
            || !Guid.TryParse(actorId, out var actorUserId)
            || actorUserId == Guid.Empty
            || !companyContext.IsResolved
            || companyContext.CompanyId != requestedCompany
            || !currentUser.CanAccessCompany(requestedCompany))
        {
            return false;
        }

        fence = new Fence(requestedCompany, actorId, actorUserId);
        return true;
    }

    /// <summary>The context did not move while the directory was read.</summary>
    private bool StillFenced(Fence fence) =>
        TryFence(fence.CompanyId, fence.ActorId, out var current) && current == fence;

    private static bool TryReadResolve(UserDirectoryResolveRequestV1? request, out Guid[] userIds)
    {
        userIds = [];
        if (request?.UserIds is null
            || request.UserIds.Count is 0 or > UserDirectoryLimitsV1.MaximumResolveCount
            || request.UserIds.Any(id => id == Guid.Empty))
        {
            return false;
        }

        var distinct = request.UserIds.Distinct().ToArray();
        if (distinct.Length != request.UserIds.Count)
        {
            return false;
        }

        userIds = distinct;
        return true;
    }

    private static bool TryReadSearch(UserDirectorySearchRequestV1? request, out string? query, out int limit,
        out string? holdingPermission)
    {
        query = null;
        limit = 0;
        holdingPermission = null;
        if (request is null || request.Limit is < 1 or > UserDirectoryLimitsV1.MaximumSearchLimit)
        {
            return false;
        }

        // SAT-D27: a named permission must be a registered one, spelled exactly; anything else refuses, never widens.
        if (request.HoldingPermission is { } permission
            && (string.IsNullOrWhiteSpace(permission)
                || !string.Equals(permission, permission.Trim(), StringComparison.Ordinal)
                || !ErpPermissions.IsKnown(permission)))
        {
            return false;
        }

        var trimmed = request.Query?.Trim();
        if (trimmed is { Length: > UserDirectoryLimitsV1.MaximumQueryLength })
        {
            return false;
        }

        query = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        limit = request.Limit;
        holdingPermission = request.HoldingPermission;
        return true;
    }

    private readonly record struct Fence(Guid CompanyId, string ActorId, Guid ActorUserId);
}
