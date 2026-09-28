using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Modules.Identity.Infrastructure.Shell;

/// <summary>
/// AT-004: the two read-only screens of "Kullanıcılar ve yetkiler" — the users list with the selected user's
/// assignments (SY-01 read subset) and one role's permissions and holders (SY-02 read subset). Both stand on
/// <see cref="IdentityPermissionManifestV1.AssignmentsRead"/> alone, company-wide, in one company: the view is
/// fenced to the server-resolved company, and a group-wide view is not decided.
/// <para>
/// No control is declared: there is no assign, revoke or edit surface (NEEDS-SINAN #66 5738113623, OPEN-013).
/// A declaration grants nothing; the grant of the read is a human gate. The central registry requires registered
/// names, so the host registers this provider only after the central catalogue knows the name, together with its
/// accepted revision. The ids and routes are the frontend lane's (<c>src/identity/capabilities.ts</c>).
/// </para>
/// </summary>
public sealed class IdentityShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "IDENTITY";
    public const string OwnerContractVersion = "1.0";

    public const string AccessListCapabilityId = "identity.assignments.list";
    public const string AccessListRouteId = "identity.assignments.list";
    public const string AccessListRoutePath = "/ayarlar/kullanicilar-ve-yetkiler";
    public const string AccessListScreenId = "SY-01";
    public const string RoleDetailCapabilityId = "identity.roles.detail";
    public const string RoleDetailRouteId = "identity.roles.detail";
    public const string RoleDetailRoutePath = "/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId";
    public const string RoleDetailScreenId = "SY-02";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(
            AccessListCapabilityId,
            AccessListRouteId,
            AccessListRoutePath,
            AccessListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [IdentityPermissionManifestV1.AssignmentsRead],
            ImplementationAvailable: true),
        new ShellCapabilityDeclaration(
            RoleDetailCapabilityId,
            RoleDetailRouteId,
            RoleDetailRoutePath,
            RoleDetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [IdentityPermissionManifestV1.AssignmentsRead],
            ImplementationAvailable: true),
    ];
}
