using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S5/S6: one name per persistent family (the Sales quote policies and the e-Fatura scenario policy) — each
/// registered once centrally, company-wide only, NOT Critical, and handed to nobody by the shipped seed (a grant is a human
/// gate).
/// </summary>
public sealed class SettingsSalesQuoteAndEInvoicePolicyPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Theory]
    [InlineData(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage,
        "settings.sales-quote-policies.manage", ErpPermissions.SettingsSalesQuotePoliciesManage)]
    [InlineData(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage,
        "settings.einvoice-scenarios.manage", ErpPermissions.SettingsEInvoiceScenariosManage)]
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
        Assert.DoesNotContain(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage, seed,
            StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage, seed,
            StringComparison.Ordinal);
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
