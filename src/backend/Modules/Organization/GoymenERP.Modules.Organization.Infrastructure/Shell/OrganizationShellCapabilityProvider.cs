using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Organization.Infrastructure.Shell;

public sealed class OrganizationShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "CHATGPT-BUSINESS/ORGANIZATION";

    /// <summary>
    /// 1.1 adds the ORG-03 work-center list route. The four 1.0 declarations are
    /// byte-identical; only the owner contract version and the new route move the
    /// registry revision, which the API host's accepted authority pins.
    /// </summary>
    public const string OwnerContractVersion = "1.1";

    public const string ListCapabilityId = "organization.sites.list";
    public const string DetailCapabilityId = "organization.locations.detail";
    public const string SitesReadCapabilityId = "organization.sites.read";
    public const string StatusCapabilityId = "organization.locations.status";
    public const string WorkCenterListCapabilityId = "organization.work-centers.list";
    public const string WorkCenterListRoutePath = "/ayarlar/organizasyon/is-merkezleri";

    public string Owner => OwnerId;
    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        // ORG-01/02/03 are the existing frontend's technical subviews of AV-01,
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

        // ORG-03 — the authorized work-center list, over the already merged
        // GET /api/v1/organization/work-centers. It is a SEPARATE route rather than a
        // section of ORG-01 because it stands on a DIFFERENT authority: the endpoint
        // authorizes `organization.work-centers.read`, and ORG-01's route stands on
        // `organization.locations.read` alone. Folding the work centers into ORG-01
        // would close a list this user is entitled to whenever the locations read is
        // missing — the exact failure #246 removed on the sites side. So this route
        // requires the work-centers permission and NOTHING else; the step from a work
        // center to its location is gated separately, by the ORG-02 capability above.
        //
        // No detail/status capability comes with it: there is no work-center detail or
        // status endpoint, and inventing one here would publish a route to nothing.
        var workCenters = new ShellCapabilityDeclaration(
            WorkCenterListCapabilityId, "organization.work-center-list",
            WorkCenterListRoutePath, "ORG-03",
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
