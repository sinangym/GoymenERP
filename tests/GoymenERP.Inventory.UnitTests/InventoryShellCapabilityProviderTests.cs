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
        Assert.Equal(10, declarations.Count);
        var declaration = Assert.Single(declarations,
            item => item.CapabilityId == InventoryShellCapabilityProvider.OpeningStockCapabilityId);

        Assert.Equal("INVENTORY", provider.Owner);
        Assert.Equal("1.8", provider.ContractVersion);
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

        Assert.Equal(8, declarations.Length);
        Assert.All(declarations, declaration =>
            Assert.Contains(ErpPermissions.ProductOperationalSelectionRead, declaration.RequiredPermissions));
        var openingStock = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.OpeningStockCapabilityId);
        Assert.Equal([ErpPermissions.InventoryOpeningStockRecord], openingStock.RequiredPermissions);
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
    public void Allocation_detail_is_a_dormant_top_level_route_over_only_existing_company_wide_write()
    {
        var declaration = Assert.Single(new InventoryShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == InventoryShellCapabilityProvider.AllocationDetailCapabilityId);

        Assert.Equal("inventory.allocations.detail", declaration.CapabilityId);
        Assert.Equal("inventory.allocations.detail", declaration.RouteId);
        Assert.Equal("/stok/tahsisler/:allocationId", declaration.RoutePath);
        Assert.Equal("AL-04", declaration.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, declaration.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Equal([ErpPermissions.InventoryPhysicalAllocationWrite], declaration.RequiredPermissions);
        Assert.False(declaration.ImplementationAvailable);
        Assert.Null(declaration.ParentCapabilityId);
        Assert.Null(declaration.GlobalProductOperation);
    }

    [Fact]
    public void Allocation_detail_does_not_change_the_existing_available_capability_set()
    {
        var available = new InventoryShellCapabilityProvider().GetDeclarations()
            .Where(item => item.ImplementationAvailable)
            .Select(item => item.CapabilityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            InventoryShellCapabilityProvider.StockPositionsCapabilityId,
            InventoryShellCapabilityProvider.PhysicalAllocationCapabilityId,
            InventoryShellCapabilityProvider.LabelTransformationCapabilityId,
            InventoryShellCapabilityProvider.LabelClassificationCapabilityId,
            InventoryShellCapabilityProvider.StockCountRecordCapabilityId,
            InventoryShellCapabilityProvider.StockCountApproveCapabilityId,
            InventoryShellCapabilityProvider.StockScrapRecordCapabilityId,
            InventoryShellCapabilityProvider.StockScrapApproveCapabilityId,
            InventoryShellCapabilityProvider.OpeningStockCapabilityId,
        ];

        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToArray(), available);
        Assert.DoesNotContain(InventoryShellCapabilityProvider.AllocationDetailCapabilityId, available);
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
