using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Inventory.Infrastructure.Shell;

/// <summary>
/// Publishes the one opening-stock workspace accepted for INV-OPEN-2. The home, create and detail
/// React routes intentionally share this capability: the shell has one module entry and the API
/// independently enforces the same existing company-wide permission. Publishing grants nothing.
/// </summary>
public sealed class InventoryShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "INVENTORY";
    public const string OwnerContractVersion = "1.1";
    public const string StockPositionsCapabilityId = "inventory.stock-positions.list";
    public const string StockPositionsRouteId = "inventory.stock-positions";
    public const string StockPositionsRoutePath = "/stok/pozisyonlar";
    public const string StockPositionsScreenId = "ST-01";
    public const string OpeningStockCapabilityId = ErpPermissions.InventoryOpeningStockRecord;
    public const string OpeningStockRouteId = "inventory.opening-stock";
    public const string OpeningStockRoutePath = "/stok/acilis";
    public const string OpeningStockScreenId = "INV-OPEN-2";

    public string Owner => OwnerId;
    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new(
            StockPositionsCapabilityId,
            StockPositionsRouteId,
            StockPositionsRoutePath,
            StockPositionsScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
            ImplementationAvailable: true),
        new(
            OpeningStockCapabilityId,
            OpeningStockRouteId,
            OpeningStockRoutePath,
            OpeningStockScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.InventoryOpeningStockRecord],
            ImplementationAvailable: true),
    ];
}
