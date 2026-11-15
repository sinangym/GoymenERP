using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// KIM-D9 / ID-3: <c>identity.users.manage</c> is REGISTERED, not granted. It is company-wide and not Critical; no seeded
/// role holds it and handing it to a role is a human gate. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityUsersManagePermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_name_is_spelled_as_the_decision_spells_it_and_the_manifest_carries_it()
    {
        Assert.Equal("identity.users.manage", ErpPermissions.IdentityUsersManage);
        Assert.Equal(ErpPermissions.IdentityUsersManage, IdentityPermissionManifestV1.UsersManage);
        Assert.Contains(IdentityPermissionManifestV1.UsersManage, IdentityPermissionManifestV1.Names);
    }

    [Fact]
    public void The_name_is_registered_once_company_wide_and_not_critical()
    {
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.IdentityUsersManage));
        Assert.Single(ErpPermissions.All, name => name == ErpPermissions.IdentityUsersManage);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.IdentityUsersManage));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.IdentityUsersManage));
    }

    [Fact]
    public void The_ID_3_command_names_are_registered_and_critical()
    {
        foreach (var permission in new[] { IdentityPermissionManifestV1.RolesManage, IdentityPermissionManifestV1.AccessApprove })
        {
            Assert.True(ErpPermissions.IsKnown(permission));
            Assert.True(ErpPermissions.IsCritical(permission));
            Assert.Contains(permission, IdentityPermissionManifestV1.Names);
        }
    }

    [Fact]
    public void No_seeded_role_carries_it_in_the_shipped_seed()
    {
        var seedText = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));

        Assert.DoesNotContain(ErpPermissions.IdentityUsersManage, seedText, StringComparison.Ordinal);
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seedText, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SeedPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }
}
