namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Inventory-owned server-truth read for the physical allocations of one Sales order line.
/// Publication as an HTTP route remains central-owned.
/// </summary>
public interface IInventoryPhysicalAllocationReadsV1
{
    Task<InventoryPhysicalAllocationReadsV1> SearchAsync(
        InventoryPhysicalAllocationReadQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryPhysicalAllocationReadQueryV1(
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    int Limit = 100);

public sealed record InventoryPhysicalAllocationReadsV1(
    IReadOnlyList<InventoryPhysicalAllocationReadItemV1> Items,
    bool SearchWindowFull);

/// <summary>
/// The allocation is the same V1 shape returned by create/release. LabelVersion is the exact
/// captured label version required by a subsequent label-bound mutation.
/// </summary>
public sealed record InventoryPhysicalAllocationReadItemV1(
    InventoryPhysicalAllocationViewV1 Allocation,
    string LabelVersion);

public static class InventoryPhysicalAllocationReadCodesV1
{
    public const string Unauthenticated = "inventory_physical_allocation_read_unauthenticated";
    public const string Forbidden = "inventory_physical_allocation_read_forbidden";
    public const string InvalidQuery = "inventory_physical_allocation_read_invalid_query";
    public const string ReadConflict = "inventory_physical_allocation_read_conflict";
    public const string Unavailable = "inventory_physical_allocation_read_unavailable";
}

/// <summary>Closed read failure channel. Denials disclose no allocation, label or company facts.</summary>
public sealed class InventoryPhysicalAllocationReadException(
    int statusCode,
    string code,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
