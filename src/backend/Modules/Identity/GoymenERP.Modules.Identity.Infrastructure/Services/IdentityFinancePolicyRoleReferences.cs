using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>Owner catalog read for No 133; a K3-reviewed purpose boundary, never a role assignment or grant.</summary>
public sealed class IdentityFinancePolicyRoleReferences(IdentityDbContext database, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority) : IIdentityFinancePolicyRoleReferencesV1
{
    private const string Prepare = "settings.policies.prepare";
    private const int MaximumRoles = 512; // Bound the response; overflow is unavailable, never a partial catalog.

    public async Task<IdentityFinancePolicyRoleReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        await FenceAsync(companyId, actor, cancellationToken);
        // Role definitions are the existing global catalog; company-scoped authority is measured before and after.
        // One SQL statement snapshots the role's real permission set as well as its identity and active state.
        var rows = await database.Roles.AsNoTracking().Where(x => x.IsActive).Include(x => x.Permissions)
            .AsSingleQuery().OrderBy(x => x.Id).Take(MaximumRoles + 1).ToArrayAsync(cancellationToken);
        await FenceAsync(companyId, actor, cancellationToken);
        if (rows.Length > MaximumRoles || rows.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Code)
            || string.IsNullOrWhiteSpace(x.Name) || x.Permissions.Any(p => string.IsNullOrWhiteSpace(p.PermissionCode))))
            throw new InvalidOperationException("identity_finance_policy_source_unavailable");
        return new IdentityFinancePolicyRoleReferencesV1(companyId, rows.Select(x => new IdentityFinancePolicyRoleReferenceV1(x.Id, x.Code, x.Name,
            "identity.role-policy-source:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                x.Id, x.Code, x.Name, x.IsActive, x.Version,
                Permissions = x.Permissions.Select(p => p.PermissionCode).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            }))).ToLowerInvariant())).ToArray());
    }

    private async Task FenceAsync(Guid companyId, string? actor, CancellationToken cancellationToken)
    {
        Same(companyId, actor, cancellationToken);
        if (!ErpPermissions.IsKnown(Prepare)) throw new InvalidOperationException("identity_finance_policy_source_unavailable");
        var scope = await authority.RevalidatePermissionScopeAsync(Prepare, companyId, cancellationToken);
        Same(companyId, actor, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
    }

    private void Same(Guid companyId, string? actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException();
    }
}
