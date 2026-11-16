namespace GoymenERP.Modules.Inventory.Application;

/// <summary>A bounded source choice. Quantity strings are the purchase unit; the server supplies exact availability.</summary>
public sealed record InventoryFutureSupplyOptionV1(Guid PurchaseOrderId, Guid PurchaseOrderLineId, int LineNo,
    string SupplierCode, string SupplierName, Guid UnitId, string UnitCode,
    string AllocatableQuantity, string ReservedQuantity, string AvailableQuantity);

public sealed record InventoryFutureSupplyOptionsV1(Guid CompanyId, Guid SalesOrderId, Guid SalesOrderLineId,
    Guid ProductId, string ProductVersion, Guid DemandUnitId, string DemandUnitCode, string OpenDemandQuantity,
    IReadOnlyList<InventoryFutureSupplyOptionV1> Items, string? NextCursor);
