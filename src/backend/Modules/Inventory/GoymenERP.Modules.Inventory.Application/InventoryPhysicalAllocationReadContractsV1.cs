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

    /// <summary>
    /// INV-AL-1 change: the physical sales allocations of one label (newest first, active and past) with their
    /// release facts and change history, and the label version a change must name. Same authority as
    /// <see cref="SearchAsync"/>; an unknown or another company's label is 404.
    /// </summary>
    Task<InventoryLabelPhysicalAllocationsV1> ReadLabelAsync(
        Guid labelId,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryLabelPhysicalAllocationsV1(
    Guid LabelId,
    string LabelVersion,
    IReadOnlyList<InventoryLabelPhysicalAllocationV1> Items,
    bool WindowFull);

/// <summary>
/// One allocation of the label: the V1 allocation, when and why it was released (null while not released), and
/// every change that touched it, oldest first — its own resizes, the move that released it and/or the move that
/// created it. History is append-only; nothing here is ever rewritten.
/// </summary>
public sealed record InventoryLabelPhysicalAllocationV1(
    InventoryPhysicalAllocationViewV1 Allocation,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason,
    IReadOnlyList<InventoryPhysicalAllocationChangeEntryV1> Changes);

public sealed record InventoryPhysicalAllocationChangeEntryV1(
    Guid ChangeId,
    string Kind,
    Guid SourceAllocationId,
    string SourceVersion,
    Guid SourceLabelId,
    string SourceRemainingKg,
    Guid TargetAllocationId,
    Guid TargetLabelId,
    string TargetRemainingKg,
    string Reason,
    DateTimeOffset ChangedAtUtc);

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

/// <summary>
/// Company-scoped exact-ID sales-line allocation truth under the existing physical-allocation-write authority.
/// This port does not scan or enlarge the line/label windows. Unknown/foreign IDs are 404.
/// </summary>
public interface IInventoryPhysicalAllocationExactReadsV1
{
    Task<InventoryPhysicalAllocationExactReadV1> ReadAsync(
        Guid allocationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Captured allocation, label version and persisted stock-position/release facts. No loading-hold
/// or binding value is inferred; those facts are outside the current Inventory source contract.
/// </summary>
public sealed record InventoryPhysicalAllocationExactReadV1(
    InventoryPhysicalAllocationViewV1 Allocation,
    string LabelVersion,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason);

public static class InventoryPhysicalAllocationReadCodesV1
{
    public const string Unauthenticated = "inventory_physical_allocation_read_unauthenticated";
    public const string Forbidden = "inventory_physical_allocation_read_forbidden";
    public const string InvalidQuery = "inventory_physical_allocation_read_invalid_query";
    public const string NotFound = "inventory_physical_allocation_read_not_found";
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
