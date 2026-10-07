namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Stable owner declaration for the ST-01 stock-position read surface.</summary>
public static class InventoryStockPositionApiV1
{
    public const string PositionsPath = "/api/v1/inventory/positions";
    public const string GetPositionsOperationId = "GetInventoryStockPositionsV1";
    public const string ProductDisplayContractHeader = "X-ERP-Inventory-Stock-Display";
    public const string ProductDisplayContractVersion = "1.0";
}

/// <summary>
/// Company-scoped, bounded physical-stock read. It reports stored stock components and active
/// physical allocation remainders; it does not reserve, move, value or project stock.
/// </summary>
public interface IInventoryStockPositionsV1
{
    Task<InventoryStockPositionsV1> SearchAsync(
        InventoryStockPositionQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryStockPositionQueryV1(
    string? Q = null,
    int Limit = 25,
    string? Cursor = null,
    Guid? ProductId = null,
    Guid? LocationId = null,
    InventoryLabelClassificationV1? Classification = null,
    string? OwnerKind = null,
    Guid? OwnerId = null);

public sealed record InventoryStockPositionsV1(
    IReadOnlyList<InventoryStockPositionV1> Items,
    bool SearchWindowFull,
    string? NextCursor = null);

/// <summary>
/// One physical label at its single stored place. Quantities are lossless kilogram text.
/// AvailableKg is FreePhysicalKg minus active physical-allocation RemainingKg. It is an observed
/// component, not a promise, reservation or future-supply value.
/// </summary>
public sealed record InventoryStockPositionV1(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? Classification,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg);

public static class InventoryStockPositionCodesV1
{
    public const string Unauthenticated = "inventory_stock_position_unauthenticated";
    public const string Forbidden = "inventory_stock_position_forbidden";
    public const string InvalidQuery = "inventory_stock_position_invalid_query";
    public const string ReadConflict = "inventory_stock_position_read_conflict";
    public const string Unavailable = "inventory_stock_position_unavailable";
}

/// <summary>Closed read failure channel. Denials disclose no stock or company facts.</summary>
public sealed class InventoryStockPositionException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
