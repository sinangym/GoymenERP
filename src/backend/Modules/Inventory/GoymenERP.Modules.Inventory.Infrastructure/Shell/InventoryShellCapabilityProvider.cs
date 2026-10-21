using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Shell;

/// <summary>
/// Publishes the one opening-stock workspace accepted for INV-OPEN-2. The home, create and detail
/// React routes intentionally share this capability: the shell has one module entry and the API
/// independently enforces the same existing company-wide permission. Publishing grants nothing.
/// </summary>
public sealed class InventoryShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "INVENTORY";
    public const string OwnerContractVersion = "1.7";
    public const string StockPositionsCapabilityId = "inventory.stock-positions.list";
    public const string StockPositionsRouteId = "inventory.stock-positions";
    public const string StockPositionsRoutePath = "/stok/pozisyonlar";
    public const string StockPositionsScreenId = "ST-01";
    public const string PhysicalAllocationCapabilityId = ErpPermissions.InventoryPhysicalAllocationWrite;

    // R03.3 exact-ID reads reuse this existing control's company-wide permission.
    // AL-04 frontend route/capability publication is a separate owner gate: the
    // backend read port does not advertise a new available React workspace here.

    /// <summary>
    /// R19 split/merge: a CONTROL of the label detail reached from ST-01 (no route of its own). It keeps the
    /// parent's route, context and scope and adds only the command name, which the API re-measures on every
    /// command. Publishing grants nothing.
    /// </summary>
    public const string LabelTransformationCapabilityId = ErpPermissions.InventoryLabelTransformationRecord;
    public const string LabelClassificationCapabilityId = ErpPermissions.InventoryLabelClassificationWrite;

    /// <summary>
    /// AT-037 stock count: two CONTROLS of the label detail reached from ST-01 (no route of their own), one to
    /// record a count and one to decide a pending difference. Separate names keep maker ≠ checker visible in the
    /// shell; the API re-measures each on every command. Publishing grants nothing.
    /// </summary>
    public const string StockCountRecordCapabilityId = ErpPermissions.InventoryStockCountRecord;
    public const string StockCountApproveCapabilityId = ErpPermissions.InventoryStockCountApprove;

    /// <summary>
    /// ST-02 scrap write-off: two CONTROLS of the label detail reached from ST-01 (no route of their own), one to
    /// record a scrap and one to decide it, as the stock count's pair (maker ≠ checker visible in the shell). The API
    /// re-measures each on every command. Publishing grants nothing. The names are the owner's manifest literals until
    /// the central catalogue registers them.
    /// </summary>
    public const string StockScrapRecordCapabilityId = ErpPermissions.InventoryStockScrapRecord;
    public const string StockScrapApproveCapabilityId = ErpPermissions.InventoryStockScrapApprove;
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
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
            ],
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
                ErpPermissions.ProductOperationalSelectionRead,
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
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryLabelTransformationRecord,
            ],
        },
        stockPositions with
        {
            CapabilityId = LabelClassificationCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryLabelClassificationWrite,
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
                ErpPermissions.ProductOperationalSelectionRead,
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
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryStockCountApprove,
            ],
        },
        stockPositions with
        {
            CapabilityId = StockScrapRecordCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryStockScrapRecord,
            ],
        },
        stockPositions with
        {
            CapabilityId = StockScrapApproveCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryStockScrapApprove,
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
