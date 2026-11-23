using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S4b: one name per persistent Sales policy family (the delivery tolerance and the approval amount threshold) — each
/// registered once centrally, company-wide only, NOT Critical, and handed to nobody by the shipped seed (a grant is a human
/// gate).
/// </summary>
public sealed class SettingsSalesPolicyPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Theory]
    [InlineData(SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage, "settings.sales-delivery-tolerances.manage",
        ErpPermissions.SettingsSalesDeliveryTolerancesManage)]
    [InlineData(SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage, "settings.sales-approval-thresholds.manage",
        ErpPermissions.SettingsSalesApprovalThresholdsManage)]
    public void The_owner_name_is_the_registered_central_name_company_wide_and_not_critical(string ownerName, string literal,
        string central)
    {
        Assert.Equal(literal, ownerName);
        Assert.Equal(ownerName, central);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, name => name == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void No_seeded_role_carries_them()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage, seed, StringComparison.Ordinal);
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
