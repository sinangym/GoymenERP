using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>KIM-D20: also fences headerless commands and fresh authority; no founder exception outside the setup command.</summary>
internal sealed class CompanyLifecycleAuthorizationGate(ErpAuthorizationService inner, ICompanyLifecycleReadPortV1 lifecycle)
    : IErpAuthorizationService, IErpAuthorityRevalidationService, IActiveRoleCodeReader
{
    private async Task<bool> ActiveAsync(Guid companyId, CancellationToken cancellationToken) =>
        companyId != Guid.Empty && await lifecycle.ReadAsync(companyId, cancellationToken)
            is { State: CompanyLifecycleStateV1.Active, Version: >= 1 } view && view.CompanyId == companyId;
    public async Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
        (await GetPermissionScopeAsync(permission, companyId, cancellationToken)).IsGranted;
    public async Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
        await ActiveAsync(companyId, cancellationToken) ? await inner.GetPermissionScopeAsync(permission, companyId, cancellationToken) : PermissionScope.Denied;
    public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
        await ActiveAsync(companyId, cancellationToken) ? await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken) : PermissionScope.Denied;
    public async Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await ActiveAsync(companyId, cancellationToken) ? await inner.GetActiveRoleCodesAsync(companyId, cancellationToken) : new HashSet<string>(StringComparer.Ordinal);
    public async Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await inner.GetAuthorizationSnapshotAsync(companyId, permissions, cancellationToken);
        var allowed = new HashSet<Guid>();
        foreach (var id in snapshot.AllowedCompanyIds.Order())
            if (await ActiveAsync(id, cancellationToken)) allowed.Add(id);
        var active = await ActiveAsync(companyId, cancellationToken);
        var scopes = snapshot.PermissionScopes.ToDictionary(x => x.Key, x => active ? x.Value : PermissionScope.Denied, StringComparer.Ordinal);
        var canonical = snapshot.Revision + ":" + active + ":" + string.Join(",", allowed.Order().Select(x => x.ToString("D")));
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(revision, allowed, scopes);
    }
}
