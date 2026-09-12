using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Organization.Infrastructure.Shell;

public sealed class OrganizationShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/ORGANIZATION";
    public const string OwnerContractVersion = "1.0";
    public const string ListCapabilityId = "organization.sites.list";
    public const string DetailCapabilityId = "organization.locations.detail";
    public const string SitesReadCapabilityId = "organization.sites.read";
    public const string StatusCapabilityId = "organization.locations.status";

    public string Owner => OwnerId;
    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        // ORG-01/02 are the existing frontend's technical subviews of AV-01,
        // not new canonical screens. The sites.list id is retained for compatibility;
        // its location list does not require the optional sites.read permission.
        var list = new ShellCapabilityDeclaration(
            ListCapabilityId, "organization.settings-list", "/ayarlar/organizasyon", "ORG-01",
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.AnyGrantedScope,
            [ErpPermissions.OrganizationLocationsRead], ImplementationAvailable: true);
        var detail = new ShellCapabilityDeclaration(
            DetailCapabilityId, "organization.location-detail",
            "/ayarlar/organizasyon/lokasyon/:locationId", "ORG-02",
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.AnyGrantedScope,
            [ErpPermissions.OrganizationLocationsRead], ImplementationAvailable: true);

        return
        [
            list,
            detail,
            list with
            {
                CapabilityId = SitesReadCapabilityId,
                ParentCapabilityId = ListCapabilityId,
                RequiredPermissions =
                    [ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationSitesRead],
            },
            detail with
            {
                CapabilityId = StatusCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions =
                    [ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationLocationsStatusUpdate],
            },
        ];
    }
}
