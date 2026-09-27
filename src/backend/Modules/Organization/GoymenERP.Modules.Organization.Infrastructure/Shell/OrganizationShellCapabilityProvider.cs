using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Organization.Infrastructure.Shell;

public sealed class OrganizationShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/ORGANIZATION";
    // 1.0 -> 1.1 (ORG-03): the owner contract version enters the canonical form of EVERY registration
    // this provider makes, so publishing the work-centre list moves the registry revision of the four
    // older declarations too. That is why the accepted authority constants move in the same head: the
    // registry constructor throws when the computed revision is not an accepted one, so a head that
    // carried the declaration with the old constants would not start the API at all.
    // 1.1 -> 1.2 (ORG-04): the work-centre card. The same cascade as ORG-03: the owner contract version enters the
    // canonical form of every registration, so the accepted authority constants move with it in the central round.
    // 1.2 -> 1.3 (master edit): five controls — site create/edit, location create, location edit, work-centre create,
    // work-centre edit — each on its screen's own parent and each adding its record kind's own write permission. The
    // same cascade: the accepted authority constants move with it in the central round.
    public const string OwnerContractVersion = "1.3";
    public const string ListCapabilityId = "organization.sites.list";
    public const string DetailCapabilityId = "organization.locations.detail";
    public const string SitesReadCapabilityId = "organization.sites.read";
    public const string StatusCapabilityId = "organization.locations.status";
    public const string WorkCenterListCapabilityId = "organization.work-centers.list";
    public const string WorkCenterDetailCapabilityId = "organization.work-centers.detail";
    public const string SitesManageCapabilityId = "organization.sites.manage";
    public const string LocationCreateCapabilityId = "organization.locations.create";
    public const string LocationEditCapabilityId = "organization.locations.edit";
    public const string WorkCenterCreateCapabilityId = "organization.work-centers.create";
    public const string WorkCenterEditCapabilityId = "organization.work-centers.edit";

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
        // ORG-04 stands on organization.work-centers.read alone, like ORG-03: the card is the list row's record, and
        // the location link inside it is carried per record by CanOpenLocationDetail, never by this declaration.
        var workCenterDetail = new ShellCapabilityDeclaration(
            WorkCenterDetailCapabilityId, "organization.work-center-detail",
            "/ayarlar/organizasyon/is-merkezleri/:workCenterId", "ORG-04",
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.AnyGrantedScope,
            [ErpPermissions.OrganizationWorkCentersRead], ImplementationAvailable: true);

        return
        [
            list,
            detail,
            workCenters,
            workCenterDetail,
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
            // Master edit controls. Each stands on its parent screen's read, the read of whatever it must list to
            // offer a choice (the site list for a new location, the location list for a new work centre), and its
            // own record kind's write. The server re-authorizes every command on the write alone and its row scope.
            list with
            {
                CapabilityId = SitesManageCapabilityId,
                ParentCapabilityId = ListCapabilityId,
                RequiredPermissions =
                [
                    ErpPermissions.OrganizationLocationsRead,
                    ErpPermissions.OrganizationSitesRead,
                    OrganizationPermissionManifestV1.SitesWrite,
                ],
            },
            list with
            {
                CapabilityId = LocationCreateCapabilityId,
                ParentCapabilityId = ListCapabilityId,
                RequiredPermissions =
                [
                    ErpPermissions.OrganizationLocationsRead,
                    ErpPermissions.OrganizationSitesRead,
                    OrganizationPermissionManifestV1.LocationsWrite,
                ],
            },
            detail with
            {
                CapabilityId = LocationEditCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions =
                    [ErpPermissions.OrganizationLocationsRead, OrganizationPermissionManifestV1.LocationsWrite],
            },
            workCenters with
            {
                CapabilityId = WorkCenterCreateCapabilityId,
                ParentCapabilityId = WorkCenterListCapabilityId,
                RequiredPermissions =
                [
                    ErpPermissions.OrganizationWorkCentersRead,
                    ErpPermissions.OrganizationLocationsRead,
                    OrganizationPermissionManifestV1.WorkCentersWrite,
                ],
            },
            workCenterDetail with
            {
                CapabilityId = WorkCenterEditCapabilityId,
                ParentCapabilityId = WorkCenterDetailCapabilityId,
                RequiredPermissions =
                    [ErpPermissions.OrganizationWorkCentersRead, OrganizationPermissionManifestV1.WorkCentersWrite],
            },
        ];
    }
}
