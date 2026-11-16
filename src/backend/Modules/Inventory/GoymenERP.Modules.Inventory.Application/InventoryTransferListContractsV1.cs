namespace GoymenERP.Modules.Inventory.Application;

public sealed record InventoryTransferListItemV1(Guid TransferId, Guid CompanyId, string Version, string Status,
    Guid SourceLocationId, Guid DestinationLocationId, DateTimeOffset PlannedAtUtc, DateTimeOffset? DispatchedAtUtc);
public sealed record InventoryTransferListV1(Guid CompanyId, IReadOnlyList<InventoryTransferListItemV1> Items, bool WindowFull);

public sealed record InventoryTransferDisplayItemV1(InventoryTransferListItemV1 Transfer,
    string SourceLocationCode, string SourceLocationName, string DestinationLocationCode, string DestinationLocationName);
public sealed record InventoryTransferDisplayListV1(Guid CompanyId, IReadOnlyList<InventoryTransferDisplayItemV1> Items, bool WindowFull);
