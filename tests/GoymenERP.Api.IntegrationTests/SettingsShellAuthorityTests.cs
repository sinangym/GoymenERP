using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D61…D70: the Settings provider (the discount limit screen) joins the full normal host as its last module, with
/// one capability on the one non-Critical, company-wide limit name; MOD-03 (Settings 1.1) adds the tax master screen on its
/// own non-Critical, company-wide name.
/// </summary>
public sealed class SettingsShellAuthorityTests
{
    [Fact]
    public void Normal_host_revision_matches_the_exact_integrated_provider_tuple_with_settings()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(),
            new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(),
            new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(),
            new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(),
            new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(),
            new InventoryShellCapabilityProvider(),
            new IdentityShellCapabilityProvider(),
            new ProductPriceListShellCapabilityProvider(),
            new SettingsShellCapabilityProvider(),
        ];
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);

        Assert.True(string.Equals(
                ShellCapabilityRegistryAuthorities.FullNormalHostWithProductPriceListsSalesQuoteExpiryApprovalAndSettingsV11,
                actual,
                StringComparison.Ordinal),
            $"Settings normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_screen_stands_on_the_one_limit_name_company_wide_in_one_company()
    {
        var declaration = Assert.Single(new SettingsShellCapabilityProvider().GetDeclarations(),
            candidate => candidate.CapabilityId == SettingsShellCapabilityProvider.SalesDiscountLimitsCapabilityId);

        Assert.Equal(("settings.sales-discount-limits.manage", "/ayarlar/iskonto-limitleri", "AY-ISK-01"),
            (declaration.CapabilityId, declaration.RoutePath, declaration.ScreenId));
        Assert.Equal([SettingsPermissionManifestV1.SalesDiscountLimitsManage], declaration.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.False(ErpPermissions.IsCritical(SettingsPermissionManifestV1.SalesDiscountLimitsManage));
    }

    [Fact]
    public void The_tax_master_screen_stands_on_the_one_tax_master_name_company_wide_in_one_company()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations();
        Assert.Equal(2, declarations.Count);
        Assert.Equal("1.1", new SettingsShellCapabilityProvider().ContractVersion);
        var declaration = Assert.Single(declarations,
            candidate => candidate.CapabilityId == SettingsShellCapabilityProvider.TaxMasterCapabilityId);

        Assert.Equal(("settings.tax-master.manage", "/ayarlar/vergi", "AY-VRG-01"),
            (declaration.CapabilityId, declaration.RoutePath, declaration.ScreenId));
        Assert.Equal([SettingsTaxMasterPermissionManifestV1.TaxMasterManage], declaration.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.False(ErpPermissions.IsCritical(SettingsTaxMasterPermissionManifestV1.TaxMasterManage));
    }
}
