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
    public const string OwnerContractVersion = "1.4";
    public const string StockPositionsCapabilityId = "inventory.stock-positions.list";
    public const string StockPositionsRouteId = "inventory.stock-positions";
    public const string StockPositionsRoutePath = "/stok/pozisyonlar";
    public const string StockPositionsScreenId = "ST-01";
    public const string PhysicalAllocationCapabilityId = ErpPermissions.InventoryPhysicalAllocationWrite;

    /// <summary>
    /// R19 split/merge: a CONTROL of the label detail reached from ST-01 (no route of its own). It keeps the
    /// parent's route, context and scope and adds only the command name, which the API re-measures on every
    /// command. Publishing grants nothing.
    /// </summary>
    public const string LabelTransformationCapabilityId = ErpPermissions.InventoryLabelTransformationRecord;

    /// <summary>
    /// AT-037 stock count: two CONTROLS of the label detail reached from ST-01 (no route of their own), one to
    /// record a count and one to decide a pending difference. Separate names keep maker ≠ checker visible in the
    /// shell; the API re-measures each on every command. Publishing grants nothing.
    /// </summary>
    public const string StockCountRecordCapabilityId = ErpPermissions.InventoryStockCountRecord;
    public const string StockCountApproveCapabilityId = ErpPermissions.InventoryStockCountApprove;
    public const string OpeningStockCapabilityId = ErpPermissions.InventoryOpeningStockRecord;
    public const string OpeningStockRouteId = "inventory.opening-stock";
    public const string OpeningStockRoutePath = "/stok/acilis";
    public const string OpeningStockScreenId = "INV-OPEN-2";

    public string Owner => OwnerId;
    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var stockPositions = new ShellCapabilityDeclaration(
            StockPositionsCapabilityId,
            StockPositionsRouteId,
            StockPositionsRoutePath,
            StockPositionsScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
            ImplementationAvailable: true);

        return
        [
        stockPositions,
        stockPositions with
        {
            CapabilityId = PhysicalAllocationCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.InventoryPhysicalAllocationWrite,
            ],
        },
        stockPositions with
        {
            CapabilityId = LabelTransformationCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.InventoryLabelTransformationRecord,
            ],
        },
        stockPositions with
        {
            CapabilityId = StockCountRecordCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.InventoryStockCountRecord,
            ],
        },
        stockPositions with
        {
            CapabilityId = StockCountApproveCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.InventoryStockCountApprove,
            ],
        },
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
}
