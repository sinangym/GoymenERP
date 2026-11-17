using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsPurchaseInvoiceVariancePolicyPermissionRegistrationTests
{
    [Fact]
    public void Purchase_invoice_policy_authority_is_registered_once_and_company_wide_without_a_seeded_grant()
    {
        const string permission = SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage;
        Assert.Equal("settings.purchase-invoice-variance-policies.manage", permission);
        Assert.True(ErpPermissions.IsKnown(permission));
        Assert.Single(ErpPermissions.All, value => value == permission);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(permission));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        Assert.DoesNotContain(permission, File.ReadAllText(Path.Combine(directory.FullName,
            "src/backend/GoymenERP.Api/Seed/identity-seed.json")), StringComparison.Ordinal);
    }
}
