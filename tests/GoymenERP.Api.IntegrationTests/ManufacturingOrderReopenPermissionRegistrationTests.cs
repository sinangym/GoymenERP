using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingOrderReopenPermissionRegistrationTests
{
    [Fact]
    public void The_reopen_name_is_registered_once_company_wide_and_neither_granted_nor_marked_critical()
    {
        const string name = "manufacturing.orders.reopen";
        Assert.Equal(name, ErpPermissions.ManufacturingOrdersReopen);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, item => item == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
        Assert.Equal(ErpPermissions.All.Where(item => item.StartsWith("manufacturing.", StringComparison.Ordinal)).Order(),
            ManufacturingPermissionManifestV1.Names.Order());
        var control = Assert.Single(new ManufacturingShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == ManufacturingShellCapabilityProvider.ReopenCapabilityId);
        Assert.False(control.ImplementationAvailable);
        Assert.Contains(name, control.RequiredPermissions);
        Assert.Contains(ErpPermissions.AccountingPeriodsRead, control.RequiredPermissions);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var seed = File.ReadAllText(Path.Combine(directory.FullName, "src/backend/GoymenERP.Api/Seed/identity-seed.json"));
        Assert.DoesNotContain(name, seed, StringComparison.Ordinal);
    }
}
