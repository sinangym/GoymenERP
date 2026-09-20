using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Organization.Infrastructure.Shell;

public sealed class OrganizationShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/ORGANIZATION";
    // 1.0 -> 1.1 (ORG-03): the owner contract version enters the canonical form of EVERY registration
    // this provider makes, so publishing the work-centre list moves the registry revision of the four
    // older declarations too. That is why the accepted authority constants move in the same head: the
    // registry constructor throws when the computed revision is not an accepted one, so a head that
    // carried the declaration with the old constants would not start the API at all.
    public const string OwnerContractVersion = "1.1";
    public const string ListCapabilityId = "organization.sites.list";
    public const string DetailCapabilityId = "organization.locations.detail";
    public const string SitesReadCapabilityId = "organization.sites.read";
    public const string StatusCapabilityId = "organization.locations.status";
    public const string WorkCenterListCapabilityId = "organization.work-centers.list";

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
        // ORG-03 stands on organization.work-centers.read ALONE, and deliberately not on
        // organization.locations.read as well. The two are separate grants on the server
        // (OrganizationService.GetWorkCentersAsync vs GetLocationAsync ask for different permissions),
        // so requiring both here would hide the work-centre list from a reader who is entitled to it.
        // The consequence of holding only one is already carried in the row itself: CanOpenLocationDetail
        // (#377) answers false, so the screen shows the row without a link that would only 404 at the card.
        var workCenters = new ShellCapabilityDeclaration(
            WorkCenterListCapabilityId, "organization.work-center-list",
            "/ayarlar/organizasyon/is-merkezleri", "ORG-03",
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.AnyGrantedScope,
            [ErpPermissions.OrganizationWorkCentersRead], ImplementationAvailable: true);

        return
        [
            list,
            detail,
            workCenters,
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
