using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D65/D69: ONE separate name for editing the discount limit groups, limits and bindings — the Settings owner's name is
/// the central one, registered once, company-wide only, NOT Critical, and handed to nobody by the shipped seed (the grant to
/// the board members and the Admin persona is a human gate).
/// </summary>
public sealed class SettingsSalesDiscountLimitPermissionRegistrationTests
{
    private const string SeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";

    [Fact]
    public void The_owner_name_is_the_registered_central_name_company_wide_and_not_critical()
    {
        Assert.Equal("settings.sales-discount-limits.manage", SettingsPermissionManifestV1.SalesDiscountLimitsManage);
        Assert.Equal(SettingsPermissionManifestV1.SalesDiscountLimitsManage, ErpPermissions.SettingsSalesDiscountLimitsManage);
        Assert.True(ErpPermissions.IsKnown(SettingsPermissionManifestV1.SalesDiscountLimitsManage));
        Assert.Single(ErpPermissions.All, name => name == SettingsPermissionManifestV1.SalesDiscountLimitsManage);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(SettingsPermissionManifestV1.SalesDiscountLimitsManage));
        Assert.False(ErpPermissions.IsCritical(SettingsPermissionManifestV1.SalesDiscountLimitsManage));
        // Keep the exact non-Manufacturing registry inventory, including the approved F-029 and W6-W8 owners.
        // Each owner's registration tests separately protect its literal, scope, classification and no-grant boundary.
        Assert.Equal([
                "settings.ai-gateway.manage",
                "settings.bank-csv-mappings.manage",
                "settings.collateral-types.manage",
                "settings.document-number-series.manage",
                "settings.einvoice-scenarios.manage",
                "settings.exchange-rates.correct",
                "settings.exchange-rates.half-day.mark",
                "settings.exchange-rates.import",
                "settings.exchange-rates.read",
                "settings.finance-opening-period.manage",
                "settings.login-rate-limit.manage",
                "settings.money-profiles.manage",
                "settings.notification-retention.manage",
                "settings.policies.approve",
                "settings.policies.prepare",
                "settings.policies.publish",
                "settings.policies.publish-critical",
                "settings.production-reference-lists.manage",
                "settings.purchase-invoice-variance-policies.manage",
                "settings.reference-lists.manage",
                "settings.sales-approval-thresholds.manage",
                "settings.sales-contract-discount-limit.manage",
                "settings.sales-delivery-tolerances.manage",
                "settings.sales-discount-limits.manage",
                "settings.sales-quote-policies.manage",
                "settings.segregation-of-duties.manage",
                "settings.tax-master.manage",
                "settings.unit-conversion-policies.manage",
                "settings.unit-profiles.manage",
                "settings.warehouse-transfer-scale.manage",
                "settings.work-approval-duration.manage",
                "settings.work-center-capacities.manage"],
            ErpPermissions.All.Where(name => name.StartsWith("settings.", StringComparison.Ordinal)
                    && !name.StartsWith("settings.manufacturing-", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_seeded_role_carries_it()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SeedPath));
        Assert.Contains(ErpPermissions.OrganizationLocationsRead, seed, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingsPermissionManifestV1.SalesDiscountLimitsManage, seed, StringComparison.Ordinal);
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
