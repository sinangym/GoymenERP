using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// The first W3 allocation source supported by the owner predecessor. The host must obtain these
/// facts from <c>ISalesOrderLineReferencePortV1</c>; client supplied product or approval facts are
/// never authoritative.
/// </summary>
public static class InventoryAllocationDemandKindsV1
{
    public const string SalesOrderLine = "sales.order-line";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePhysicalSalesAllocationV1(
    [property: JsonRequired] Guid AllocationId,
    [property: JsonRequired] Guid SalesOrderId,
    [property: JsonRequired] Guid SalesOrderLineId,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] string ProductVersion,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string QuantityKg,
    [property: JsonRequired] string Reason,
    DateTimeOffset? ReservedUntilUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleasePhysicalAllocationV1(
    [property: JsonRequired] string ExpectedAllocationVersion,
    [property: JsonRequired] string Reason);

/// <summary>
/// INV-AL-1 change of one active physical sales allocation, as the host derives it from the approved Sales line
/// (<see cref="ProductId"/>/<see cref="ProductVersion"/> are the line's, never the caller's). The target label is the
/// allocation's own label for a quantity change (in place: same allocation id, <see cref="NewAllocationId"/> null) or
/// another label of the same product for a move (the source allocation is released and <see cref="NewAllocationId"/>
/// reserves the target in the same save). <see cref="QuantityKg"/> is the kilograms the allocation holds afterwards.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePhysicalSalesAllocationV1(
    [property: JsonRequired] string ExpectedAllocationVersion,
    [property: JsonRequired] Guid SalesOrderId,
    [property: JsonRequired] Guid SalesOrderLineId,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] string ProductVersion,
    [property: JsonRequired] Guid TargetLabelId,
    [property: JsonRequired] string ExpectedTargetLabelVersion,
    [property: JsonRequired] string QuantityKg,
    [property: JsonRequired] string Reason,
    Guid? NewAllocationId);

public static class InventoryAllocationChangeKindsV1
{
    /// <summary>The same allocation, at the same position, now holds another quantity.</summary>
    public const string Resize = "Resize";

    /// <summary>The source allocation was released and a new one reserves another label of the same product.</summary>
    public const string Move = "Move";
}

/// <summary>
/// The answer to a change: <see cref="Allocation"/> is the active allocation that now holds the demand's kilograms;
/// <see cref="Replaced"/> is the released source allocation of a move (null for a resize).
/// </summary>
public sealed record InventoryPhysicalAllocationChangeViewV1(
    string Kind,
    InventoryPhysicalAllocationViewV1 Allocation,
    InventoryPhysicalAllocationViewV1? Replaced);

public sealed record InventoryPhysicalAllocationChangeResultV1(
    InventoryPhysicalAllocationChangeViewV1 Change,
    bool IsReplay,
    string OriginalCorrelationId);

public sealed record InventoryPhysicalAllocationViewV1(
    Guid AllocationId,
    Guid CompanyId,
    string Version,
    string Status,
    Guid LabelId,
    Guid ProductId,
    string DemandKind,
    Guid DemandId,
    Guid DemandLineId,
    string DemandProductVersion,
    string AllocatedKg,
    string RemainingKg,
    string Reason,
    DateTimeOffset? ReservedUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record InventoryPhysicalAllocationResultV1(
    InventoryPhysicalAllocationViewV1 Allocation,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// Module-owned persistence boundary for physical sales allocation. It is deliberately not a host
/// route: the host must first verify the approved Sales line and fresh write authority. No Inventory
/// permission name or grant is invented here. The writer must invoke the required authority fence
/// after its last asynchronous read, before staging effects, and before releasing a stored replay.
/// </summary>
public interface IInventoryPhysicalAllocationWriterV1
{
    Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        CreatePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        Guid allocationId,
        ChangePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

public static class InventoryAllocationCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string InvalidReference = "inventory_allocation_invalid_reference";
    public const string LabelVersionConflict = "inventory_label_version_conflict";
    public const string AllocationVersionConflict = "inventory_allocation_version_conflict";
    public const string AllocationStateConflict = "inventory_allocation_state_conflict";
    public const string InsufficientFreeStock = "inventory_allocation_insufficient_free_stock";

    /// <summary>
    /// 422: the label's Free position is not owned AND held by this company (e.g. customer-owned or at a fason
    /// supplier), so a Sales order line cannot reserve it (<see cref="InventorySalesOwnershipGuardV1"/>).
    /// </summary>
    public const string OwnerNotCompany = "inventory_allocation_owner_not_company";
    /// <summary>422: the change would leave the allocation exactly as it is (same label, same kilograms).</summary>
    public const string ChangeUnchanged = "inventory_allocation_change_unchanged";
    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string ValueDoesNotFit = "inventory_value_does_not_fit";
}

public sealed class InventoryAllocationException(
    int status,
    string code,
    bool isReplay = false,
    string? originalCorrelationId = null,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}
