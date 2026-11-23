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

    /// <summary>LOJ-D1..D5: a customer's processed goods reserved for its own subcontract service-order line.</summary>
    public const string SubcontractServiceOrderLine = "sales.subcontract-service-order-line";

    /// <summary>
    /// URT-D3/D21: a Manufacturing plan's input, held from plan approval until the input issue consumes it or the
    /// plan cancel / order close releases the remainder. DemandId is the plan, DemandLineId the input label.
    /// </summary>
    public const string ManufacturingPlanInput = "manufacturing.plan-input";
}

/// <summary>
/// A physical sales allocation of one label for the approved Sales line. <see cref="IsSubstitute"/> (KLN-D39) is the
/// user's confirmation that a label of ANOTHER product ("muadil") holds the line: the writer re-verifies on the server
/// that the label's product is similar within the Settings tolerance (KLN-D40/D70) and refuses otherwise; with no
/// tolerance configured only the line's own product is allowed. A label of the line's own product never carries the flag.
/// <see cref="PieceCount"/> (KLN-D119, SAT-D434; AK-4b) is the number of pieces reserved with the kilograms: required when
/// the label's free stock tracks pieces (omitted → 422 <see cref="InventoryAllocationCodesV1.PieceCountRequired"/>,
/// fail-closed) and refused on a kg-only label (<see cref="InventoryAllocationCodesV1.PieceCountNotTracked"/>, no backfill).
/// </summary>
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
    DateTimeOffset? ReservedUntilUtc,
    bool IsSubstitute = false,
    int? PieceCount = null);

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
/// <see cref="PieceCount"/> (KLN-D119, AK-4b) is the pieces it holds afterwards: required when the allocation (resize) or
/// the target label's free stock (move) tracks pieces, refused otherwise.
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
    Guid? NewAllocationId,
    int? PieceCount = null);

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
    DateTimeOffset UpdatedAtUtc,
    bool IsSubstitute = false,
    // KLN-D119 (AK-4b): the reserved and still-held pieces of an allocation on a piece-tracking label; omitted on an
    // allocation without pieces, so every stored result and audit payload of such an allocation keeps its bytes.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AllocatedPieces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RemainingPieces = null);

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

    Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        Guid allocationId,
        ChangePhysicalSalesAllocationV1 request,
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

    /// <summary>
    /// 422: the label's Free position is not owned AND held by this company (e.g. customer-owned or at a fason
    /// supplier), so a Sales order line cannot reserve it (<see cref="InventorySalesOwnershipGuardV1"/>).
    /// </summary>
    public const string OwnerNotCompany = "inventory_allocation_owner_not_company";
    /// <summary>422: the change would leave the allocation exactly as it is (same label, same kilograms).</summary>
    public const string ChangeUnchanged = "inventory_allocation_change_unchanged";

    /// <summary>
    /// 422 (KLN-D39/D40/D70): the request confirmed a substitute ("muadil") label but no similar-product tolerance is
    /// configured in Settings, the label's product is not similar to the line's product within it, or the label is of the
    /// line's own product. A label of another product without the flag stays <see cref="InvalidReference"/>.
    /// </summary>
    public const string SubstituteNotSimilar = "inventory_allocation_substitute_not_similar";
    /// <summary>422 (KLN-D119): the label's free stock (or the allocation) tracks pieces and the request names none. Fail-closed.</summary>
    public const string PieceCountRequired = "inventory_allocation_piece_count_required";

    /// <summary>422 (KLN-D119): a piece count on a label or allocation without pieces; a count is never backfilled.</summary>
    public const string PieceCountNotTracked = "inventory_allocation_piece_count_not_tracked";

    /// <summary>422 (KLN-D119): the pieces and the kilograms would not run out together (the last kilograms take the last pieces).</summary>
    public const string PieceCountInvalid = "inventory_allocation_piece_count_invalid";

    /// <summary>409 (KLN-D119): the label has fewer unreserved pieces than requested.</summary>
    public const string InsufficientFreePieces = "inventory_allocation_insufficient_free_pieces";
    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string ValueDoesNotFit = "inventory_value_does_not_fit";

    /// <summary>
    /// 409 (KLN-D65 (1)/(2)): physical + active future allocations of the Sales line, with this request, would exceed the line's
    /// open quantity (KLN-D28 (4)). Nothing is written; existing allocations stay as they are.
    /// </summary>
    public const string ExceedsDemandOpenQuantity = "inventory_allocation_exceeds_demand_open_quantity";

    /// <summary>
    /// 422 (KLN-D66 (3)): the cap must compare in kilograms but no central conversion source can bring the Sales order unit (or a
    /// future allocation's purchase unit) to kilograms for this product. Fail-closed, never a guess.
    /// </summary>
    public const string UnitConversionUnavailable = "inventory_allocation_unit_conversion_unavailable";
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
