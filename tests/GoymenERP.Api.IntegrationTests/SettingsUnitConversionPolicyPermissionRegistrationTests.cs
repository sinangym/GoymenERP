using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-004: the unit conversion policy name — registered once centrally, company-wide only, NOT Critical, and handed to nobody by
/// the shipped seed (a grant is a human gate).
/// </summary>
public sealed class SettingsUnitConversionPolicyPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_owner_name_is_the_registered_central_name_company_wide_and_not_critical()
    {
        const string ownerName = SettingsUnitConversionPolicyPermissionManifestV1.Manage;
        Assert.Equal("settings.unit-conversion-policies.manage", ownerName);
        Assert.Equal(ownerName, ErpPermissions.SettingsUnitConversionPoliciesManage);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, name => name == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void No_seeded_role_carries_it()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsUnitConversionPolicyPermissionManifestV1.Manage, seed, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
