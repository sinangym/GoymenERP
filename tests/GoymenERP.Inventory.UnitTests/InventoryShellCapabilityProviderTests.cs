using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryShellCapabilityProviderTests
{
    [Fact]
    public void Opening_stock_workspace_reuses_the_existing_permission_and_one_route()
    {
        var provider = new InventoryShellCapabilityProvider();
        var declaration = Assert.Single(provider.GetDeclarations());

        Assert.Equal("INVENTORY", provider.Owner);
        Assert.Equal("1.0", provider.ContractVersion);
        Assert.Equal(ErpPermissions.InventoryOpeningStockRecord, declaration.CapabilityId);
        Assert.Equal("inventory.opening-stock", declaration.RouteId);
        Assert.Equal("/stok/acilis", declaration.RoutePath);
        Assert.Equal("INV-OPEN-2", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal([ErpPermissions.InventoryOpeningStockRecord], declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Null(declaration.ParentCapabilityId);
    }
}
