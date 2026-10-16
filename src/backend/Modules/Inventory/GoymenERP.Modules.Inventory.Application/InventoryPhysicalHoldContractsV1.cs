namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Stable owner and state tokens of the first whole-label physical-hold contract.</summary>
public static class InventoryPhysicalHoldTokensV1
{
    public const string QualityOwner = "Quality";
    public const string QualityInspectionSource = "QualityInspection";
    public const string Active = "Active";
    public const string Released = "Released";
}

/// <summary>Stable failures of Inventory's whole-label physical-hold boundary.</summary>
public static class InventoryPhysicalHoldCodesV1
{
    public const string ValidationFailed = "inventory_physical_hold_validation_failed";
    public const string NotFound = "inventory_physical_hold_not_found";
    public const string LabelNotFound = "inventory_physical_hold_label_not_found";
    public const string LabelVersionConflict = "inventory_physical_hold_label_version_conflict";
    public const string HoldVersionConflict = "inventory_physical_hold_version_conflict";
    public const string HoldConflict = "inventory_physical_hold_conflict";
    public const string IdempotencyConflict = "inventory_physical_hold_idempotency_conflict";
    public const string PositionConflict = "inventory_physical_hold_position_conflict";
    public const string LegacyBlockedAmbiguous = "inventory_physical_hold_legacy_blocked_ambiguous";
    public const string OwnerConflict = "inventory_physical_hold_owner_conflict";
    public const string SelfApproval = "inventory_physical_hold_self_approval";
    public const string AlreadyReleased = "inventory_physical_hold_already_released";
    public const string ConcurrentChange = "inventory_physical_hold_concurrent_change";
    public const string Unavailable = "inventory_physical_hold_unavailable";
}

/// <summary>
/// Quality asks Inventory to block one real label. There is deliberately no quantity: Inventory owns
/// the stored position and moves the label's complete physical kilograms or refuses the command.
/// </summary>
public sealed record PlaceInventoryPhysicalHoldV1(
    Guid HoldId,
    Guid LabelId,
    string ExpectedLabelVersion,
    Guid InspectionId,
    string Reason,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Quality asks Inventory to close exactly its own hold after a maker-checker disposition. Reject is
/// not a release request and creates no Inventory movement in this contract.
/// </summary>
public sealed record ReleaseInventoryPhysicalHoldV1(
    Guid HoldId,
    string ExpectedHoldVersion,
    Guid LabelId,
    string ExpectedLabelVersion,
    Guid DispositionId,
    string Reason);

/// <summary>The durable Inventory-owned view of one whole-label hold.</summary>
public sealed record InventoryPhysicalHoldViewV1(
    Guid HoldId,
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string Owner,
    string Source,
    Guid SourceId,
    string QuantityKg,
    string State,
    string Version,
    string Reason,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleasedBy,
    Guid? ReleaseDispositionId,
    string? ReleaseReason);

public sealed record InventoryPhysicalHoldResultV1(
    InventoryPhysicalHoldViewV1 Hold,
    bool Replayed,
    string CorrelationId);

/// <summary>
/// The Inventory-owned write boundary. Authorization and Quality disposition persistence are caller
/// concerns; this boundary still enforces company, version, idempotency, physical and maker-checker invariants.
/// </summary>
public interface IInventoryPhysicalHoldWriterV1
{
    Task<InventoryPhysicalHoldResultV1> PlacePhysicalHoldAsync(
        PlaceInventoryPhysicalHoldV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryPhysicalHoldResultV1> ReleasePhysicalHoldAsync(
        ReleaseInventoryPhysicalHoldV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryPhysicalHoldException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
