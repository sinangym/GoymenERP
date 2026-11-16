using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryShellCapabilityProviderTests
{
    [Fact]
    public void Opening_stock_workspace_reuses_the_existing_permission_and_one_route()
    {
        var provider = new InventoryShellCapabilityProvider();
        var declarations = provider.GetDeclarations();
        Assert.Equal(12, declarations.Count);
        var declaration = Assert.Single(declarations,
            item => item.CapabilityId == InventoryShellCapabilityProvider.OpeningStockCapabilityId);

        Assert.Equal("INVENTORY", provider.Owner);
        Assert.Equal("1.9", provider.ContractVersion);
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

    [Fact]
    public void Future_allocation_read_and_write_keep_their_own_route_and_permissions_separate_from_physical_stock()
    {
        var declarations = new InventoryShellCapabilityProvider().GetDeclarations();
        var read = Assert.Single(declarations, item => item.CapabilityId == InventoryShellCapabilityProvider.FutureAllocationReadCapabilityId);
        var write = Assert.Single(declarations, item => item.CapabilityId == InventoryShellCapabilityProvider.FutureAllocationWriteCapabilityId);
        Assert.Equal([ErpPermissions.InventoryFutureAllocationRead], read.RequiredPermissions);
        Assert.Equal([ErpPermissions.InventoryFutureAllocationRead, ErpPermissions.InventoryFutureAllocationWrite], write.RequiredPermissions);
        Assert.Equal(read.CapabilityId, write.ParentCapabilityId);
        Assert.Equal(InventoryShellCapabilityProvider.FutureAllocationRoutePath, read.RoutePath);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, read.RoutePrecedence);
        Assert.True(read.ImplementationAvailable);
    }

    [Fact]
    public void Stock_position_read_is_a_static_company_wide_technical_capability_over_existing_permissions()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.StockPositionsCapabilityId);

        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
            ],
            declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Null(declaration.ParentCapabilityId);
    }

    [Fact]
    public void Stock_position_route_and_every_child_require_the_existing_Product_display_permission()
    {
        var declarations = new InventoryShellCapabilityProvider().GetDeclarations()
            .Where(item => item.CapabilityId == InventoryShellCapabilityProvider.StockPositionsCapabilityId
                || item.ParentCapabilityId == InventoryShellCapabilityProvider.StockPositionsCapabilityId)
            .ToArray();

        Assert.Equal(9, declarations.Length);
        Assert.All(declarations, declaration =>
            Assert.Contains(ErpPermissions.ProductOperationalSelectionRead, declaration.RequiredPermissions));
        var openingStock = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.OpeningStockCapabilityId);
        Assert.Equal([ErpPermissions.InventoryOpeningStockRecord], openingStock.RequiredPermissions);
    }

    [Fact]
    public void Transfer_cancel_control_keeps_the_existing_permission_and_fails_closed_without_real_owner_gate()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.TransferCancelCapabilityId);

        Assert.Equal("inventory.stock-transfer.cancel", declaration.CapabilityId);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, declaration.ParentCapabilityId);
        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryStockTransferCancel,
            ],
            declaration.RequiredPermissions);
        Assert.False(declaration.ImplementationAvailable);
    }

    [Fact]
    public void Physical_allocation_write_is_a_stock_position_control_over_existing_permissions()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.PhysicalAllocationCapabilityId);

        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryPhysicalAllocationWrite,
            ],
            declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, declaration.ParentCapabilityId);
    }

    [Fact]
    public void Label_transformation_record_is_a_stock_position_control_over_its_own_command_name()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.LabelTransformationCapabilityId);

        Assert.Equal("inventory.label-transformation.record", declaration.CapabilityId);
        Assert.Equal(InventoryPermissionManifestV1.LabelTransformationRecord, declaration.CapabilityId);
        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryLabelTransformationRecord,
            ],
            declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, declaration.ParentCapabilityId);
    }

    [Fact]
    public void Label_classification_write_is_a_route_less_stock_position_control_over_its_own_command_name()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.LabelClassificationCapabilityId);

        Assert.Equal("inventory.label-classification.write", declaration.CapabilityId);
        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryLabelClassificationWrite,
            ],
            declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, declaration.ParentCapabilityId);
    }

    [Theory]
    [InlineData(InventoryShellCapabilityProvider.StockCountRecordCapabilityId, "inventory.stock-count.record", InventoryPermissionManifestV1.StockCountRecord)]
    [InlineData(InventoryShellCapabilityProvider.StockCountApproveCapabilityId, "inventory.stock-count.approve", InventoryPermissionManifestV1.StockCountApprove)]
    [InlineData(InventoryShellCapabilityProvider.StockScrapRecordCapabilityId, "inventory.stock-scrap.record", InventoryPermissionManifestV1.StockScrapRecord)]
    [InlineData(InventoryShellCapabilityProvider.StockScrapApproveCapabilityId, "inventory.stock-scrap.approve", InventoryPermissionManifestV1.StockScrapApprove)]
    public void Stock_count_and_scrap_record_and_approve_are_separate_stock_position_controls_over_their_own_names(
        string capabilityId, string expectedName, string manifestName)
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == capabilityId);

        Assert.Equal(expectedName, declaration.CapabilityId);
        Assert.Equal(manifestName, declaration.CapabilityId);
        Assert.Equal("inventory.stock-positions", declaration.RouteId);
        Assert.Equal("/stok/pozisyonlar", declaration.RoutePath);
        Assert.Equal("ST-01", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.Static, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal(
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                expectedName,
            ],
            declaration.RequiredPermissions);
        Assert.True(declaration.ImplementationAvailable);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, declaration.ParentCapabilityId);
    }
}
