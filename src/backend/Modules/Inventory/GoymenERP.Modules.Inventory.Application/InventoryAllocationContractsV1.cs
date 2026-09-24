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
/// permission name or grant is invented here.
/// </summary>
public interface IInventoryPhysicalAllocationWriterV1
{
    Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        CreatePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata metadata,
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
