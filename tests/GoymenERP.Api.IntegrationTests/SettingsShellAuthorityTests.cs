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
/// own non-Critical, company-wide name; F-025 (Settings 1.2, B-11) adds the exchange rate screen on the read name with three
/// command controls (correction, half-day mark, import — the import name also carries the re-verification); F-029 S4b
/// (Settings 1.3, URN-D2/D3) adds the delivery tolerance and the approval amount threshold screens, each on its own
/// non-Critical, company-wide name; KIM-D34/KIM-D37 (Settings 1.4) add the group-wide login attempt limit and the unit
/// conversion policy screens, each on its own non-Critical, company-wide name.
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
                ShellCapabilityRegistryAuthorities.FullNormalHostWithProductPriceListsSalesReportsAndSettingsV11,
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
        Assert.Equal(10, declarations.Count);
        Assert.Equal("1.4", new SettingsShellCapabilityProvider().ContractVersion);
        var declaration = Assert.Single(declarations,
            candidate => candidate.CapabilityId == SettingsShellCapabilityProvider.TaxMasterCapabilityId);

        Assert.Equal(("settings.tax-master.manage", "/ayarlar/vergi", "AY-VRG-01"),
            (declaration.CapabilityId, declaration.RoutePath, declaration.ScreenId));
        Assert.Equal([SettingsTaxMasterPermissionManifestV1.TaxMasterManage], declaration.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.False(ErpPermissions.IsCritical(SettingsTaxMasterPermissionManifestV1.TaxMasterManage));
    }

    [Fact]
    public void The_exchange_rate_screen_stands_on_the_read_name_with_three_command_controls()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations();
        var screen = Assert.Single(declarations,
            candidate => candidate.CapabilityId == SettingsShellCapabilityProvider.ExchangeRatesCapabilityId);

        Assert.Equal(("settings.exchange-rates.read", "/ayarlar/kurlar", "AY-KUR-01"),
            (screen.CapabilityId, screen.RoutePath, screen.ScreenId));
        Assert.Equal([SettingsExchangeRatePermissionManifestV1.Read], screen.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, screen.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, screen.ScopeRequirement);
        Assert.Null(screen.ParentCapabilityId);

        string[] commands =
        [
            SettingsExchangeRatePermissionManifestV1.Correct,
            SettingsExchangeRatePermissionManifestV1.MarkHalfDay,
            SettingsExchangeRatePermissionManifestV1.Import,
        ];
        foreach (var command in commands)
        {
            var control = Assert.Single(declarations, candidate => candidate.CapabilityId == command);
            Assert.Equal(SettingsShellCapabilityProvider.ExchangeRatesCapabilityId, control.ParentCapabilityId);
            Assert.Equal([SettingsExchangeRatePermissionManifestV1.Read, command], control.RequiredPermissions);
            Assert.Equal((screen.RoutePath, screen.ScreenId), (control.RoutePath, control.ScreenId));
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, control.ContextRequirement);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, control.ScopeRequirement);
        }

        foreach (var name in commands.Prepend(SettingsExchangeRatePermissionManifestV1.Read))
        {
            Assert.False(ErpPermissions.IsCritical(name));
        }
    }

    [Fact]
    public void The_sales_policy_screens_stand_each_on_its_one_name_company_wide_in_one_company()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations();
        (string Id, string Path, string ScreenId, string Permission)[] screens =
        [
            ("settings.sales-delivery-tolerances.manage", "/ayarlar/teslim-toleransi", "AY-TSL-01",
                SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage),
            ("settings.sales-approval-thresholds.manage", "/ayarlar/onay-esikleri", "AY-ONY-01",
                SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage),
            ("settings.login-rate-limit.manage", "/ayarlar/giris-deneme-siniri", "AY-GDS-01",
                SettingsLoginRateLimitPermissionManifestV1.Manage),
            ("settings.unit-conversion-policies.manage", "/ayarlar/birim-cevrim-politikasi", "AY-BCP-01",
                SettingsUnitConversionPolicyPermissionManifestV1.Manage),
        ];

        foreach (var expected in screens)
        {
            var declaration = Assert.Single(declarations, candidate => candidate.CapabilityId == expected.Id);
            Assert.Equal((expected.Id, expected.Id, expected.Path, expected.ScreenId),
                (declaration.CapabilityId, declaration.RouteId, declaration.RoutePath, declaration.ScreenId));
            Assert.Equal([expected.Permission], declaration.RequiredPermissions);
            Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.ParentCapabilityId);
            Assert.True(declaration.ImplementationAvailable);
            Assert.False(ErpPermissions.IsCritical(expected.Permission));
            Assert.True(ErpPermissions.CompanyWideOnly.Contains(expected.Permission));
        }
    }
}
