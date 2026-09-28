using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SY-01/SY-02 read-only access view: the Identity owner's name (IdentityPermissionManifestV1) is the central
/// catalogue's name, registered Critical and company-wide only, like identity.audit.read (user decision 2026-09-28).
/// The decision grants it only to the disposable c02 access-admin actors; nothing here grants anything.
/// </summary>
public sealed class IdentityAssignmentsReadPermissionRegistrationTests
{
    [Fact]
    public void The_owner_name_is_the_registered_central_name_critical_and_company_wide_only()
    {
        Assert.Equal(IdentityPermissionManifestV1.AssignmentsRead, ErpPermissions.IdentityAssignmentsRead);
        Assert.True(ErpPermissions.IsKnown(IdentityPermissionManifestV1.AssignmentsRead));
        Assert.Single(ErpPermissions.All, known => known == IdentityPermissionManifestV1.AssignmentsRead);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(IdentityPermissionManifestV1.AssignmentsRead));
        Assert.True(ErpPermissions.IsCritical(IdentityPermissionManifestV1.AssignmentsRead));
    }

    [Fact]
    public void Every_owner_name_is_registered()
    {
        Assert.All(IdentityPermissionManifestV1.Names, name => Assert.True(ErpPermissions.IsKnown(name)));
    }
}
