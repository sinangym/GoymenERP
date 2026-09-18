using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Shell;

/// <summary>
/// Publishes the three Sales draft screens of #260 (S-02 list, S-03 create, S-04 detail) to the
/// shell — STAGED: every declaration is <c>ImplementationAvailable: false</c> until the backend
/// routes, the shell mount and the browser suite <c>s03</c> have been accepted, when a separate
/// exact-head change flips them (the Cari pattern; the Sales documents carry no other activation
/// rule). Company permissions, the Counterparty way: the list and the detail share
/// <see cref="SalesPermissionManifestV1.DraftsRead"/>, the create screen needs
/// <see cref="SalesPermissionManifestV1.DraftsCreate"/>; selecting a product on it still needs the
/// Product owner's narrow selection read, measured by that owner. The ids and routes are the
/// frontend lane's (<c>src/sales/capabilities.ts</c>); a contract test pins them to that file.
/// The central registry requires registered names, so the host registers this provider only after
/// the central catalogue knows them, together with its own accepted revision (one module = one row
/// in the optional providers and one normal-host authority).
/// </summary>
public sealed class SalesShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "SALES";
    public const string OwnerContractVersion = "1.0";

    public const string ListCapabilityId = "sales.drafts.list";
    public const string ListRouteId = "sales.drafts.list";
    public const string ListRoutePath = "/satis/siparisler";
    public const string ListScreenId = "S-02";
    public const string CreateCapabilityId = "sales.drafts.create";
    public const string CreateRouteId = "sales.drafts.create";
    public const string CreateRoutePath = "/satis/siparisler/yeni";
    public const string CreateScreenId = "S-03";
    public const string DetailCapabilityId = "sales.drafts.detail";
    public const string DetailRouteId = "sales.drafts.detail";
    public const string DetailRoutePath = "/satis/siparisler/:draftId";
    public const string DetailScreenId = "S-04";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: false),
        new ShellCapabilityDeclaration(
            CreateCapabilityId,
            CreateRouteId,
            CreateRoutePath,
            CreateScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsCreate],
            ImplementationAvailable: false),
        new ShellCapabilityDeclaration(
            DetailCapabilityId,
            DetailRouteId,
            DetailRoutePath,
            DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: false),
    ];
}
