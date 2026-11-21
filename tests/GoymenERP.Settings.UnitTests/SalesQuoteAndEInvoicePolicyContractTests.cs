using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// F-029 S5/S6 (URUN_F029 spec §5.4(2)): the Settings-side contract of the persistent quote and e-Fatura scenario policies —
/// the catalogs expose every reviewed version as the one-time baseline transfer source (unchanged values, URUN spec §4), the
/// S6 default stays EMPTY (SAT-D102), the two new families and permission names are distinct and registered, and the input
/// limits follow the stored column widths.
/// </summary>
public sealed class SalesQuoteAndEInvoicePolicyContractTests
{
    [Fact]
    public void The_quote_catalog_exposes_every_reviewed_list_and_validity_as_the_transfer_source()
    {
        var catalog = ApprovedSalesQuotePolicyCatalog.Approved;

        Assert.Equal(
            new[]
            {
                ApprovedSalesQuotePolicyCatalog.GdcLossReasonsV1,
                ApprovedSalesQuotePolicyCatalog.GspLossReasonsV1,
                ApprovedSalesQuotePolicyCatalog.GinLossReasonsV1,
            }.OrderBy(entry => entry.ProfileId),
            catalog.LossReasonLists.OrderBy(entry => entry.ProfileId));
        Assert.Equal(
            new[]
            {
                ApprovedSalesQuotePolicyCatalog.GdcDefaultValidityV1,
                ApprovedSalesQuotePolicyCatalog.GspDefaultValidityV1,
                ApprovedSalesQuotePolicyCatalog.GinDefaultValidityV1,
            }.OrderBy(entry => entry.ProfileId),
            catalog.DefaultValidities.OrderBy(entry => entry.ProfileId));
        Assert.All(catalog.LossReasonLists, list => Assert.True(catalog.Serves(list.CompanyId)));
        Assert.All(catalog.DefaultValidities, validity => Assert.True(catalog.Serves(validity.CompanyId)));
    }

    [Fact]
    public void The_scenario_catalog_transfers_the_standard_lists_and_no_default()
    {
        var catalog = ApprovedEInvoiceScenarioCatalog.Approved;

        Assert.Equal(
            new[]
            {
                ApprovedEInvoiceScenarioCatalog.GdcScenarioCodesV1,
                ApprovedEInvoiceScenarioCatalog.GspScenarioCodesV1,
                ApprovedEInvoiceScenarioCatalog.GinScenarioCodesV1,
            }.OrderBy(entry => entry.ProfileId),
            catalog.CodeLists.OrderBy(entry => entry.ProfileId));
        Assert.All(catalog.CodeLists,
            list => Assert.Equal(ApprovedEInvoiceScenarioCatalog.StandardScenarioCodes, list.Codes));
        Assert.Empty(catalog.Defaults);
    }

    [Fact]
    public void The_quote_and_scenario_families_are_new_and_distinct()
    {
        var families = new[]
        {
            SalesPolicyFamiliesV1.DeliveryTolerance,
            SalesPolicyFamiliesV1.ApprovalAmountThreshold,
            SalesPolicyFamiliesV1.SalesQuotePolicy,
            SalesPolicyFamiliesV1.EInvoiceScenario,
        };

        Assert.Equal(families.Length, families.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("SalesQuotePolicy", SalesPolicyFamiliesV1.SalesQuotePolicy);
        Assert.Equal("EInvoiceScenario", SalesPolicyFamiliesV1.EInvoiceScenario);
    }

    [Fact]
    public void The_manifest_names_are_registered_company_wide_and_distinct_from_the_S4b_names()
    {
        Assert.Equal(ErpPermissions.SettingsSalesQuotePoliciesManage,
            SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage);
        Assert.Equal(ErpPermissions.SettingsEInvoiceScenariosManage,
            SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage);
        Assert.Contains(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage, ErpPermissions.All);
        Assert.Contains(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage, ErpPermissions.All);
        Assert.Contains(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage,
            ErpPermissions.CompanyWideOnly);
        Assert.Contains(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage,
            ErpPermissions.CompanyWideOnly);
        Assert.DoesNotContain(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage,
            new[]
            {
                SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage,
                SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage,
                SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage,
            });
    }

    [Fact]
    public void The_input_limits_follow_the_stored_column_widths()
    {
        Assert.Equal(ApprovedSalesQuotePolicyCatalog.MaximumReasonCodeLength,
            SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonCodeMaximumLength);
        Assert.Equal(ApprovedSalesQuotePolicyCatalog.MaximumReasonLabelLength,
            SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonLabelMaximumLength);
        Assert.True(SalesQuoteAndEInvoicePolicyInputLimitsV1.LossReasonMaximumCount > 0);
        Assert.True(SalesQuoteAndEInvoicePolicyInputLimitsV1.ScenarioCodeMaximumCount
            >= ApprovedEInvoiceScenarioCatalog.StandardScenarioCodes.Count);
        Assert.NotEqual(SalesQuoteAndEInvoicePolicyProblemCodesV1.LossReasonIdentityConflict,
            SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList);
    }
}
