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
    public const string OwnerContractVersion = "1.13";
    // STK-S8: actual Settings/departure owners are registered by the host; their unavailable/legacy fences still apply.
    // 1.8 / STK-S18: future supply allocation read route and separately granted write control; no grant is added.
    public const string FutureAllocationReadCapabilityId = InventoryPermissionManifestV1.FutureAllocationRead;
    public const string FutureAllocationWriteCapabilityId = InventoryPermissionManifestV1.FutureAllocationWrite;
    public const string FutureAllocationRoutePath = "/stok/satis-siparisleri/:salesOrderId/satirlar/:salesOrderLineId/gelecek-ayirmalar";
    public const string StockPositionsCapabilityId = "inventory.stock-positions.list";
    public const string StockPositionsRouteId = "inventory.stock-positions";
    public const string StockPositionsRoutePath = "/stok/pozisyonlar";
    public const string StockPositionsScreenId = "ST-01";
    // 1.13 / STK-S3: explicit unused release is a standalone write-purpose route, no stock display grant.
    public const string SubcontractReleaseCapabilityId = "inventory.subcontract-allocations.release-unused";
    public const string SubcontractReleaseRoutePath = "/stok/fason-ayirma-birak";
    public const string PhysicalAllocationCapabilityId = ErpPermissions.InventoryPhysicalAllocationWrite;
    public const string TransferCancelCapabilityId = ErpPermissions.InventoryStockTransferCancel;
    public const string TransferPlanningCapabilityId = ErpPermissions.InventoryStockTransferWrite;
    public const string TransferPlanningRoutePath = "/stok/transferler/planla";
    public const string TransferOverageApprovalRoutePath = "/stok/transferler/fazla-kabul-onaylari";
    public const string TransferLossRequestRoutePath = "/stok/transferler/kayip-talepleri";
    public const string TransferLossApprovalRoutePath = "/stok/transferler/kayip-onaylari";

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

        var futureAllocations = new ShellCapabilityDeclaration(FutureAllocationReadCapabilityId, FutureAllocationReadCapabilityId,
            FutureAllocationRoutePath, "F017-D", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
            [InventoryPermissionManifestV1.FutureAllocationRead], ImplementationAvailable: true);
        return
        [
        new ShellCapabilityDeclaration(SubcontractReleaseCapabilityId, SubcontractReleaseCapabilityId,
            SubcontractReleaseRoutePath, "ST-SUBCONTRACT-UNUSED-RELEASE", ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.InventoryPhysicalAllocationWrite], ImplementationAvailable: true),
        futureAllocations,
        futureAllocations with
        {
            CapabilityId = FutureAllocationWriteCapabilityId,
            ParentCapabilityId = FutureAllocationReadCapabilityId,
            RequiredPermissions = [InventoryPermissionManifestV1.FutureAllocationRead, InventoryPermissionManifestV1.FutureAllocationWrite],
        },
        stockPositions,
        stockPositions with { CapabilityId = ErpPermissions.LogisticsTransferOutgoingDocumentsRead,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions = [.. stockPositions.RequiredPermissions, ErpPermissions.LogisticsTransferOutgoingDocumentsRead] },
        stockPositions with { CapabilityId = ErpPermissions.LogisticsDispatchNotesCreate,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions = [.. stockPositions.RequiredPermissions, ErpPermissions.LogisticsTransferOutgoingDocumentsRead, ErpPermissions.LogisticsDispatchNotesCreate] },
        stockPositions with { CapabilityId = ErpPermissions.LogisticsDispatchNotesApprove,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions = [.. stockPositions.RequiredPermissions, ErpPermissions.LogisticsTransferOutgoingDocumentsRead, ErpPermissions.LogisticsDispatchNotesApprove] },
        stockPositions with { CapabilityId = ErpPermissions.LogisticsDispatchNotesCancel,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions = [.. stockPositions.RequiredPermissions, ErpPermissions.LogisticsTransferOutgoingDocumentsRead, ErpPermissions.LogisticsDispatchNotesCancel] },
        new(TransferPlanningCapabilityId, "inventory.transfer-planning", TransferPlanningRoutePath, "STK-S8-PLAN",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ErpPermissions.InventoryStockTransferWrite], ImplementationAvailable: true),
        new(ErpPermissions.InventoryStockTransferOverageApprove, "inventory.transfer-overage-approval", TransferOverageApprovalRoutePath, "STK-S8-OVERAGE",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ErpPermissions.InventoryStockTransferOverageApprove], ImplementationAvailable: true),
        new(ErpPermissions.InventoryStockTransferLossRecord, "inventory.transfer-loss-request", TransferLossRequestRoutePath, "STK-S8-LOSS-REQUEST",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ErpPermissions.InventoryStockTransferLossRecord], ImplementationAvailable: true),
        new(ErpPermissions.InventoryStockTransferLossApprove, "inventory.transfer-loss-approval", TransferLossApprovalRoutePath, "STK-S8-LOSS-APPROVE",
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ErpPermissions.InventoryStockTransferLossApprove], ImplementationAvailable: true),
        stockPositions with
        {
            CapabilityId = TransferCancelCapabilityId,
            ParentCapabilityId = StockPositionsCapabilityId,
            RequiredPermissions =
            [
                ErpPermissions.InventoryLabelsRead,
                ErpPermissions.InventoryBalancesRead,
                ErpPermissions.ProductOperationalSelectionRead,
                ErpPermissions.InventoryStockTransferCancel,
            ],
            ImplementationAvailable = true,
        },
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
