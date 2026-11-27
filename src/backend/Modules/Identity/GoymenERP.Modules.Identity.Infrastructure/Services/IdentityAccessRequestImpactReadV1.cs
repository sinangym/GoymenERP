using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>C3 owner read increment. Reuses the native authorized reads; does not query another owner's tables or invoke
/// Settings administration (its SoD GET can close a policy). No matching of permission/role names to invented SoD pairs.</summary>
internal sealed class IdentityAccessRequestImpactReadV1(IIdentityAccessReadService reads, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority) : IIdentityAccessRequestImpactReadV1
{
    public async Task<IdentityAccessRequestImpactAnswerV1> ReadAsync(IdentityRoleRequestImpactQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Capture();
        if (scope is null) return Answer(IdentityAccessRequestImpactOutcomeV1.Denied);
        if (query is null || query.UserId == Guid.Empty || query.RoleId == Guid.Empty)
            return Answer(IdentityAccessRequestImpactOutcomeV1.Unavailable);
        try
        {
            await FenceAsync(scope, cancellationToken);
            var first = await ReadFactsAsync(scope, query, cancellationToken);
            if (first is null) return Answer(IdentityAccessRequestImpactOutcomeV1.NotFound);
            var second = await ReadFactsAsync(scope, query, cancellationToken);
            if (second is null || JsonSerializer.Serialize(first) != JsonSerializer.Serialize(second))
                return Answer(IdentityAccessRequestImpactOutcomeV1.Unavailable);
            await FenceAsync(scope, cancellationToken);
            var held = second.Current.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);
            return new(IdentityAccessRequestImpactOutcomeV1.Available, new(scope.CompanyId, query.UserId, query.RoleId,
                second.Current, second.Requested, second.Requested.Where(permission => !held.Contains(permission.Code))
                    .Select(permission => permission.Code).ToArray(),
                second.Requested.Where(permission => held.Contains(permission.Code)).Select(permission => permission.Code).ToArray(),
                second.Requested.Any(permission => permission.IsCritical), null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AccessRefused) { return Answer(IdentityAccessRequestImpactOutcomeV1.Denied); }
        catch (Exception) { return Answer(IdentityAccessRequestImpactOutcomeV1.Unavailable); }
    }

    private async Task<Facts?> ReadFactsAsync(Scope scope, IdentityRoleRequestImpactQueryV1 query, CancellationToken token)
    {
        await FenceAsync(scope, token);
        var target = await reads.GetUserAsync(query.UserId, token);
        await FenceAsync(scope, token);
        if (target is null) return null; // Native unknown/foreign/no-assignment response is unchanged.
        if (target.UserId != query.UserId || !target.IsActive) throw new InvalidOperationException("Unusable native target proof.");
        var role = await reads.GetRoleAsync(query.RoleId, token);
        await FenceAsync(scope, token);
        if (role is null) return null;
        if (role.RoleId != query.RoleId || !role.IsActive) throw new InvalidOperationException("Unusable native role proof.");
        var current = Permissions(target.EffectivePermissions.Select(permission => new IdentityAccessPermission(permission.Code, permission.IsCritical)));
        var requested = Permissions(role.Permissions);
        return new(current, requested);
    }

    private static IdentityAccessPermission[] Permissions(IEnumerable<IdentityAccessPermission> source)
    {
        var permissions = source.OrderBy(permission => permission.Code, StringComparer.Ordinal).ToArray();
        if (permissions.Any(permission => !ErpPermissions.IsKnown(permission.Code)
                || permission.IsCritical != ErpPermissions.IsCritical(permission.Code))
            || permissions.Select(permission => permission.Code).Distinct(StringComparer.Ordinal).Count() != permissions.Length)
            throw new InvalidOperationException("Unusable native permission proof.");
        return permissions;
    }

    private async Task FenceAsync(Scope scope, CancellationToken token)
    {
        RequireCurrent(scope);
        var grant = await authority.RevalidatePermissionScopeAsync(IdentityPermissionManifestV1.AssignmentsRead, scope.CompanyId, token);
        RequireCurrent(scope);
        if (grant is not { IsGranted: true, IsCompanyWide: true }) throw new AccessRefused();
    }

    private Scope? Capture() => user.IsAuthenticated && Guid.TryParse(user.UserId, out var actor) && actor != Guid.Empty
        && company.IsResolved && company.CompanyId != Guid.Empty && user.CanAccessCompany(company.CompanyId)
            ? new(company.CompanyId, user.UserId!) : null;

    private void RequireCurrent(Scope scope)
    {
        if (!user.IsAuthenticated || user.UserId != scope.ActorId || !company.IsResolved || company.CompanyId != scope.CompanyId
            || !user.CanAccessCompany(scope.CompanyId)) throw new AccessRefused();
    }

    private static IdentityAccessRequestImpactAnswerV1 Answer(IdentityAccessRequestImpactOutcomeV1 outcome) => new(outcome);
    private sealed class AccessRefused : Exception;
    private sealed record Scope(Guid CompanyId, string ActorId);
    private sealed record Facts(IdentityAccessPermission[] Current, IdentityAccessPermission[] Requested);
}
