using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// ST-02 scrap write-off (hurda/fire çıkışı) of one stocked label.
/// <list type="bullet">
/// <item><b>Decided and held here</b> (DEC-STK-007 by analogy: a stock difference needs a reason and an authority,
/// history is never deleted; AGENTS: one physical event, one movement): a scrap names one stocked label at its one
/// stored position and the kg that leave stock, positive and at most the book kg; a reason is mandatory; recording
/// needs <see cref="InventoryPermissionManifestV1.StockScrapRecord"/>; stock changes only after an approval under
/// <see cref="InventoryPermissionManifestV1.StockScrapApprove"/>, as one new <c>Scrap</c> movement whose source is the
/// scrap. A scrap that would leave less than the label's active allocations is refused, never silently breaking a
/// reservation. Nothing is deleted: the scrap, its decision and the movement are append-only.</item>
/// <item><b>Not decided (fail-closed):</b> who may approve a scrap and whether a small one may be approved by its own
/// recorder (no decision; DEC-SET-001 forbids a threshold in code). Until that is decided every scrap is decided by
/// another actor than the recorder (<see cref="InventoryStockScrapCodesV1.SelfApproval"/>). A closed reason list,
/// whether scrapped kg become a sellable scrap label (DEC-PRDCT-007) and its valuation (OPEN-008) are not decided
/// either: the reason is free text, the kg leave stock, the cost stays <c>NotCalculated</c>. Writing off stock that
/// is not the company's own in its own custody is not decided and is refused
/// (<see cref="InventoryStockScrapCodesV1.NotOwnStock"/>).</item>
/// </list>
/// </summary>
public static class InventoryStockScrapApiV1
{
    public const string Path = "/api/v1/inventory/stock-scraps";
    public const string RecordOperationId = "RecordInventoryStockScrapV1";
    public const string DecisionPathTemplate = "/api/v1/inventory/stock-scraps/{scrapId}/decision";
    public const string DecideOperationId = "DecideInventoryStockScrapV1";
    public const string LabelScrapsPathTemplate = "/api/v1/inventory/labels/{labelId}/stock-scraps";
    public const string LabelScrapsOperationId = "GetInventoryLabelStockScrapsV1";

    /// <summary>The newest scraps a label read answers; <c>windowFull</c> says older ones exist.</summary>
    public const int LabelScrapsLimit = 50;
}

/// <summary>One scrap of one stocked label, as the client sends it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordInventoryStockScrapV1(
    [property: JsonRequired] Guid ScrapId,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string ScrapKg,
    [property: JsonRequired] DateTimeOffset ScrappedAtUtc,
    [property: JsonRequired] string Reason);

/// <summary>The wire form is the name ("Approve" / "Reject").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InventoryStockScrapDecisionKindV1>))]
public enum InventoryStockScrapDecisionKindV1
{
    Approve = 1,
    Reject = 2,
}

/// <summary>The approver's decision on one pending scrap, with the approver's own note.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideInventoryStockScrapV1(
    [property: JsonRequired] InventoryStockScrapDecisionKindV1 Decision,
    [property: JsonRequired] string Reason);

/// <summary>
/// The decision on a scrap: <c>Approved</c> with the scrap movement it posted, or <c>Rejected</c> with none. Actor ids
/// stay in the audit.
/// </summary>
public sealed record InventoryStockScrapDecisionViewV1(
    string Decision,
    DateTimeOffset DecidedAtUtc,
    string Reason,
    Guid? ScrapMovementId);

/// <summary>
/// One scrap: the scrapped position as stored when recorded, the book kg the recorder saw at that label version, the
/// scrap kg, the kg that remain (canonical text), the reason and the status — <c>PendingApproval</c>,
/// <c>Approved</c> or <c>Rejected</c>.
/// </summary>
public sealed record InventoryStockScrapViewV1(
    Guid ScrapId,
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
    string ScrapKg,
    string RemainingKg,
    DateTimeOffset ScrappedAtUtc,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string Status,
    InventoryStockScrapDecisionViewV1? Decision);

public sealed record InventoryStockScrapResultV1(
    InventoryStockScrapViewV1 Scrap,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>The newest scraps of one label, newest first.</summary>
public sealed record InventoryLabelStockScrapsV1(
    Guid LabelId,
    IReadOnlyList<InventoryStockScrapViewV1> Items,
    bool WindowFull);

public static class InventoryStockScrapStatusesV1
{
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// Inventory-owned write boundary. The caller supplies a freshly authorized command envelope and a mandatory
/// callback that revalidates the same captured scope after owner reads and after the transaction flush. This
/// contract introduces no permission name, grant, route or cross-company visibility of its own.
/// </summary>
public interface IInventoryStockScrapWriterV1
{
    Task<InventoryStockScrapResultV1> RecordStockScrapAsync(
        RecordInventoryStockScrapV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryStockScrapResultV1> DecideStockScrapAsync(
        Guid scrapId,
        DecideInventoryStockScrapV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

/// <summary>Inventory-owned read boundary; null means the label is unknown in the resolved company.</summary>
public interface IInventoryStockScrapReaderV1
{
    Task<InventoryLabelStockScrapsV1?> ReadStockScrapsAsync(
        Guid labelId,
        int limit = InventoryStockScrapApiV1.LabelScrapsLimit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fenced user command: record needs <see cref="InventoryPermissionManifestV1.StockScrapRecord"/>, a decision
/// <see cref="InventoryPermissionManifestV1.StockScrapApprove"/>, each measured company-wide and fresh before the
/// writer's single transaction. The caller supplies only the transport envelope (correlation, causation,
/// Idempotency-Key, channel); actor, permission and role codes are derived from the measured scope.
/// </summary>
public interface IInventoryStockScrapCommandV1
{
    Task<InventoryStockScrapResultV1> RecordAsync(
        RecordInventoryStockScrapV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryStockScrapResultV1> DecideAsync(
        Guid scrapId,
        DecideInventoryStockScrapV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fenced read: the existing company-wide <c>inventory.labels.read</c> and <c>inventory.balances.read</c>
/// (the ST-02 label detail fence), measured before and after the owner read. No new read permission.
/// </summary>
public interface IInventoryStockScrapQueryV1
{
    Task<InventoryLabelStockScrapsV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default);
}

public static class InventoryStockScrapCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string LabelNotFound = "inventory_label_not_found";
    public const string ScrapNotFound = "inventory_stock_scrap_not_found";
    public const string LabelVersionConflict = "inventory_label_version_conflict";

    /// <summary>The label does not hold stock in exactly one stored position (none, or free and blocked parts).</summary>
    public const string PositionConflict = "inventory_stock_scrap_position_conflict";

    /// <summary>
    /// Fail-closed: the stock is not the company's own in its own custody (customer goods, or goods at a fason or
    /// external keeper). Writing such stock off is not decided.
    /// </summary>
    public const string NotOwnStock = "inventory_stock_scrap_not_own_stock";

    /// <summary>The scrap is more than the label's book kg.</summary>
    public const string ExceedsBook = "inventory_stock_scrap_exceeds_book";

    /// <summary>The scrap id is already stored with other content.</summary>
    public const string ScrapConflict = "inventory_stock_scrap_conflict";

    /// <summary>The scrap was already decided with another decision or note.</summary>
    public const string AlreadyDecided = "inventory_stock_scrap_already_decided";

    /// <summary>Fail-closed maker≠checker: the recorder cannot decide their own scrap.</summary>
    public const string SelfApproval = "inventory_stock_scrap_self_approval";

    /// <summary>The label's stock changed after the scrap was recorded; the book the recorder saw is gone.</summary>
    public const string Stale = "inventory_stock_scrap_stale";

    /// <summary>The scrap would leave less stock than the label's active allocations reserve.</summary>
    public const string AllocationConflict = "inventory_stock_scrap_allocation_conflict";

    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string Unavailable = "inventory_stock_scrap_unavailable";
}

/// <summary>
/// The CLOSED problem codes of the three routes, per HTTP status. 401/403 are platform problems. Anything else the
/// writer raises is an internal fault, never a guessed 4xx.
/// </summary>
public static class InventoryStockScrapHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = Set(
        InventoryStockScrapCodesV1.ValidationFailed,
        InventoryScaleCodes.ScaleExceeded);

    public static IReadOnlySet<string> RecordStatus404Codes { get; } = Set(InventoryStockScrapCodesV1.LabelNotFound);

    public static IReadOnlySet<string> DecideStatus404Codes { get; } = Set(InventoryStockScrapCodesV1.ScrapNotFound);

    public static IReadOnlySet<string> ReadStatus404Codes { get; } = Set(InventoryStockScrapCodesV1.LabelNotFound);

    public static IReadOnlySet<string> RecordStatus409Codes { get; } = Set(
        InventoryStockScrapCodesV1.IdempotencyConflict,
        InventoryStockScrapCodesV1.LabelVersionConflict,
        InventoryStockScrapCodesV1.PositionConflict,
        InventoryStockScrapCodesV1.ExceedsBook,
        InventoryStockScrapCodesV1.NotOwnStock,
        InventoryStockScrapCodesV1.ScrapConflict,
        InventoryStockScrapCodesV1.AllocationConflict,
        InventoryStockScrapCodesV1.ConcurrentChange);

    public static IReadOnlySet<string> DecideStatus409Codes { get; } = Set(
        InventoryStockScrapCodesV1.IdempotencyConflict,
        InventoryStockScrapCodesV1.AlreadyDecided,
        InventoryStockScrapCodesV1.SelfApproval,
        InventoryStockScrapCodesV1.Stale,
        InventoryStockScrapCodesV1.AllocationConflict,
        InventoryStockScrapCodesV1.ConcurrentChange);

    /// <summary>
    /// Provider/setup states, never the caller's input (as <see cref="InventoryStockCountHttpCodesV1"/>): the
    /// kilogram-profile answers of the Settings owner and a corrupt stored state.
    /// </summary>
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        InventoryStockScrapCodesV1.PermissionUnregistered,
        InventoryStockScrapCodesV1.Unavailable,
        InventoryScaleCodes.ProfileUnusable,
        "settings_provider_unavailable",
        SettingsPrerequisiteCodesV1.UnitProfileNotFound,
        "settings_profile_invalid",
        "settings_access_denied",
        "settings_decimal_invalid",
        "settings_decimal_scale_exceeded");

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}

public sealed class InventoryStockScrapException(
    int status,
    string code,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
