using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// ST-05 / AT-037 — stock count with difference reason and authority (DEC-STK-007, KILITLI).
/// <list type="bullet">
/// <item><b>Decided and held here:</b> a count names one stocked label at its one stored position and the kg the
/// counter found; the difference is <c>counted − book</c>; a reason is mandatory; recording needs
/// <see cref="InventoryPermissionManifestV1.StockCountRecord"/>; the difference changes stock only after an
/// approval under <see cref="InventoryPermissionManifestV1.StockCountApprove"/> (AT-037 "neden ve onay olmadan
/// kesinleşmez"), as one new <c>CountAdjustment</c> movement whose source is the count. Nothing is deleted: the
/// count, its decision and the movement are append-only, and a wrong adjustment is corrected by a new count.</item>
/// <item><b>Not decided (fail-closed):</b> which difference is "critical" and whether a small one may be approved
/// by its own counter (INV-R20-R21-01 Q2, unanswered; DEC-SET-001 forbids a threshold in code). Until that is
/// decided every non-zero difference is treated as critical: the decision must come from another actor than the
/// counter (<see cref="InventoryStockCountCodesV1.SelfApproval"/>). A closed reason list is not decided either;
/// the reason is free text.</item>
/// </list>
/// </summary>
public static class InventoryStockCountApiV1
{
    public const string Path = "/api/v1/inventory/stock-counts";
    public const string RecordOperationId = "RecordInventoryStockCountV1";
    public const string DecisionPathTemplate = "/api/v1/inventory/stock-counts/{countId}/decision";
    public const string DecideOperationId = "DecideInventoryStockCountV1";
    public const string LabelCountsPathTemplate = "/api/v1/inventory/labels/{labelId}/stock-counts";
    public const string LabelCountsOperationId = "GetInventoryLabelStockCountsV1";

    /// <summary>The newest counts a label read answers; <c>windowFull</c> says older ones exist.</summary>
    public const int LabelCountsLimit = 50;
}

/// <summary>One physical count of one stocked label, as the client sends it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordInventoryStockCountV1(
    [property: JsonRequired] Guid CountId,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string CountedKg,
    [property: JsonRequired] DateTimeOffset CountedAtUtc,
    [property: JsonRequired] string Reason);

/// <summary>The wire form is the name ("Approve" / "Reject").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InventoryStockCountDecisionKindV1>))]
public enum InventoryStockCountDecisionKindV1
{
    Approve = 1,
    Reject = 2,
}

/// <summary>The approver's decision on one pending count difference, with the approver's own note.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideInventoryStockCountV1(
    [property: JsonRequired] InventoryStockCountDecisionKindV1 Decision,
    [property: JsonRequired] string Reason);

/// <summary>
/// The decision on a count: <c>Approved</c> with the adjustment movement it posted, or <c>Rejected</c> with none.
/// Actor ids stay in the audit.
/// </summary>
public sealed record InventoryStockCountDecisionViewV1(
    string Decision,
    DateTimeOffset DecidedAtUtc,
    string Reason,
    Guid? AdjustmentMovementId);

/// <summary>
/// One count: the counted position as stored when counted, the book kg the counter saw at that label version,
/// the counted kg, the signed difference (canonical text, e.g. <c>-2.5</c>), the reason and the status —
/// <c>NoDifference</c> (closed, nothing to post), <c>PendingApproval</c>, <c>Approved</c> or <c>Rejected</c>.
/// </summary>
public sealed record InventoryStockCountViewV1(
    Guid CountId,
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string BookKg,
    string CountedKg,
    string DifferenceKg,
    DateTimeOffset CountedAtUtc,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string Status,
    InventoryStockCountDecisionViewV1? Decision);

public sealed record InventoryStockCountResultV1(
    InventoryStockCountViewV1 Count,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>The newest counts of one label, newest first.</summary>
public sealed record InventoryLabelStockCountsV1(
    Guid LabelId,
    IReadOnlyList<InventoryStockCountViewV1> Items,
    bool WindowFull);

public static class InventoryStockCountStatusesV1
{
    public const string NoDifference = "NoDifference";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// Inventory-owned write boundary. The caller supplies a freshly authorized command envelope and a mandatory
/// callback that revalidates the same captured scope after owner reads and after the transaction flush. This
/// contract introduces no permission name, grant, route or cross-company visibility of its own.
/// </summary>
public interface IInventoryStockCountWriterV1
{
    Task<InventoryStockCountResultV1> RecordStockCountAsync(
        RecordInventoryStockCountV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryStockCountResultV1> DecideStockCountAsync(
        Guid countId,
        DecideInventoryStockCountV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

/// <summary>Inventory-owned read boundary; null means the label is unknown in the resolved company.</summary>
public interface IInventoryStockCountReaderV1
{
    Task<InventoryLabelStockCountsV1?> ReadStockCountsAsync(
        Guid labelId,
        int limit = InventoryStockCountApiV1.LabelCountsLimit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fenced user command: record needs <see cref="InventoryPermissionManifestV1.StockCountRecord"/>, a decision
/// <see cref="InventoryPermissionManifestV1.StockCountApprove"/>, each measured company-wide and fresh before the
/// writer's single transaction. The caller supplies only the transport envelope (correlation, causation,
/// Idempotency-Key, channel); actor, permission and role codes are derived from the measured scope.
/// </summary>
public interface IInventoryStockCountCommandV1
{
    Task<InventoryStockCountResultV1> RecordAsync(
        RecordInventoryStockCountV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryStockCountResultV1> DecideAsync(
        Guid countId,
        DecideInventoryStockCountV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fenced read: the existing company-wide <c>inventory.labels.read</c> and <c>inventory.balances.read</c>
/// (the ST-02 label detail fence), measured before and after the owner read. No new read permission.
/// </summary>
public interface IInventoryStockCountQueryV1
{
    Task<InventoryLabelStockCountsV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default);
}

public static class InventoryStockCountCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string LabelNotFound = "inventory_label_not_found";
    public const string CountNotFound = "inventory_stock_count_not_found";
    public const string LabelVersionConflict = "inventory_label_version_conflict";

    /// <summary>The label does not hold stock in exactly one stored position (none, or free and blocked parts).</summary>
    public const string PositionConflict = "inventory_stock_count_position_conflict";

    /// <summary>The count id is already stored with other content.</summary>
    public const string CountConflict = "inventory_stock_count_conflict";

    /// <summary>The count has no difference to approve or reject.</summary>
    public const string NotPending = "inventory_stock_count_not_pending";

    /// <summary>The count was already decided with another decision or note.</summary>
    public const string AlreadyDecided = "inventory_stock_count_already_decided";

    /// <summary>Fail-closed maker≠checker: the counter cannot decide their own count difference.</summary>
    public const string SelfApproval = "inventory_stock_count_self_approval";

    /// <summary>The label's stock changed after the count; the book the counter saw is gone — count again.</summary>
    public const string Stale = "inventory_stock_count_stale";

    /// <summary>A shortage would leave less stock than the label's active allocations reserve.</summary>
    public const string AllocationConflict = "inventory_stock_count_allocation_conflict";

    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string Unavailable = "inventory_stock_count_unavailable";
}

/// <summary>
/// The CLOSED problem codes of the three routes, per HTTP status. 401/403 are platform problems. Anything else the
/// writer raises is an internal fault, never a guessed 4xx.
/// </summary>
public static class InventoryStockCountHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = Set(
        InventoryStockCountCodesV1.ValidationFailed,
        InventoryScaleCodes.ScaleExceeded);

    public static IReadOnlySet<string> RecordStatus404Codes { get; } = Set(InventoryStockCountCodesV1.LabelNotFound);

    public static IReadOnlySet<string> DecideStatus404Codes { get; } = Set(InventoryStockCountCodesV1.CountNotFound);

    public static IReadOnlySet<string> ReadStatus404Codes { get; } = Set(InventoryStockCountCodesV1.LabelNotFound);

    public static IReadOnlySet<string> RecordStatus409Codes { get; } = Set(
        InventoryStockCountCodesV1.IdempotencyConflict,
        InventoryStockCountCodesV1.LabelVersionConflict,
        InventoryStockCountCodesV1.PositionConflict,
        InventoryStockCountCodesV1.CountConflict,
        InventoryStockCountCodesV1.ConcurrentChange);

    public static IReadOnlySet<string> DecideStatus409Codes { get; } = Set(
        InventoryStockCountCodesV1.IdempotencyConflict,
        InventoryStockCountCodesV1.NotPending,
        InventoryStockCountCodesV1.AlreadyDecided,
        InventoryStockCountCodesV1.SelfApproval,
        InventoryStockCountCodesV1.Stale,
        InventoryStockCountCodesV1.AllocationConflict,
        InventoryStockCountCodesV1.ConcurrentChange);

    /// <summary>
    /// Provider/setup states, never the caller's input (as <see cref="InventoryLabelTransformationHttpCodesV1"/>):
    /// the kilogram-profile answers of the Settings owner and a corrupt stored state.
    /// </summary>
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        InventoryStockCountCodesV1.PermissionUnregistered,
        InventoryStockCountCodesV1.Unavailable,
        InventoryScaleCodes.ProfileUnusable,
        "settings_provider_unavailable",
        SettingsPrerequisiteCodesV1.UnitProfileNotFound,
        "settings_profile_invalid",
        "settings_access_denied",
        "settings_decimal_invalid",
        "settings_decimal_scale_exceeded");

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}

public sealed class InventoryStockCountException(
    int status,
    string code,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
