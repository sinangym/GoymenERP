using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D83: ONE separate name for the Settings tax master, and the Product owner's ONE name for a product's tax classes — each
/// registered once centrally, company-wide only, NOT Critical, and handed to nobody by the shipped seed (giving them to the
/// Muhasebe/Finans and Admin personas is a human gate).
/// </summary>
public sealed class SettingsTaxMasterPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Theory]
    [InlineData(SettingsTaxMasterPermissionManifestV1.TaxMasterManage, "settings.tax-master.manage", ErpPermissions.SettingsTaxMasterManage)]
    [InlineData(ProductTaxClassificationPermissionsV1.Manage, "product.tax-classification.manage", ErpPermissions.ProductTaxClassificationManage)]
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
        Assert.DoesNotContain(SettingsTaxMasterPermissionManifestV1.TaxMasterManage, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(ProductTaxClassificationPermissionsV1.Manage, seed, StringComparison.Ordinal);
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
