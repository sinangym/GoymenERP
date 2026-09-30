using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ORG master edit: the Organization owner's three write names (OrganizationPermissionManifestV1) are the central
/// catalogue's names, registered with the scope the owner asked for and none of them Critical. The user's decision of
/// 2026-09-27 grants them only to the disposable org03 browser actor; nothing here grants anything.
/// </summary>
public sealed class OrganizationMasterDataPermissionRegistrationTests
{
    [Theory]
    // A site has no parent inside the company, so the write is meaningful only company-wide.
    [InlineData(OrganizationPermissionManifestV1.SitesWrite, ErpPermissions.OrganizationSitesWrite, true)]
    // Site / location / work-centre scoped, like organization.locations.status.update.
    [InlineData(OrganizationPermissionManifestV1.LocationsWrite, ErpPermissions.OrganizationLocationsWrite, false)]
    [InlineData(OrganizationPermissionManifestV1.WorkCentersWrite, ErpPermissions.OrganizationWorkCentersWrite, false)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName, bool companyWideOnly)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.Equal(companyWideOnly, ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_write_name_is_registered_and_no_other_organization_write_exists()
    {
        Assert.Equal(
            OrganizationPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All
                .Where(name => name.StartsWith("organization.", StringComparison.Ordinal)
                    && name.EndsWith(".write", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }
}
