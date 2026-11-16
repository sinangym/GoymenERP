using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// SAT-D304 / SAT-D372: the opening-stock cost (açılış stoku maliyeti) of one (location, product) position, entered by
/// one person and approved by another.
/// <list type="bullet">
/// <item><b>Decided and held here</b> (SAT-D304 (1)-(2)): the cost is entered manually as a pending submission under
/// <see cref="InventoryPermissionManifestV1.OpeningCostSubmit"/>; it changes no cost ledger until a different person
/// approves it under <see cref="InventoryPermissionManifestV1.OpeningCostApprove"/> (giren ≠ onaylayan,
/// <see cref="InventoryOpeningCostCodesV1.SelfApproval"/>). An approval opens the moving-average position of that
/// location and product through the one deterministic core (<c>AverageCostCalculator.ApplyOpeningCost</c>) at the
/// entered unit cost for the company's own stock on hand measured at approval, in the same single save as the decision
/// and its audit. A missing cost is simply not entered: it stays "yok", never zero, so a zero cost is refused. A
/// rejection changes nothing but the submission's status. Nothing is deleted: submissions and decisions are
/// append-only.</item>
/// <item><b>Unit and currency:</b> the cost is per stock kilogram in the company's base currency, at most
/// <c>AverageCostPrecision.InternalScale</c> decimals (SAT-D265 (2)), the unit and currency of the ledger. A foreign
/// currency opening cost is not offered.</item>
/// <item><b>Not decided (fail-closed):</b> a position the ledger already tracks (for example one that became unreadable)
/// is never overwritten (<see cref="InventoryOpeningCostCodesV1.LedgerAlreadyTracked"/>); a position without the
/// company's own stock at the location cannot be costed (<see cref="InventoryOpeningCostCodesV1.NoCostedStock"/>). One
/// pending submission per position at a time (<see cref="InventoryOpeningCostCodesV1.PendingExists"/>) is a reversible
/// technical reading so that two different costs never wait for the same position. Who holds either permission is a
/// human decision; registering the names grants nothing.</item>
/// </list>
/// </summary>
public static class InventoryOpeningCostApiV1
{
    public const string Path = "/api/v1/inventory/opening-costs";
    public const string SubmitOperationId = "SubmitInventoryOpeningCostV1";
    public const string DecisionPathTemplate = "/api/v1/inventory/opening-costs/{openingCostId}/decision";
    public const string DecideOperationId = "DecideInventoryOpeningCostV1";
}

/// <summary>
/// One opening-cost submission as the client sends it. <see cref="UnitCostPerKg"/> is canonical decimal text (ASCII
/// digits, at most one '.'), positive, base currency per stock kilogram.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitInventoryOpeningCostV1(
    [property: JsonRequired] Guid OpeningCostId,
    [property: JsonRequired] Guid LocationId,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] string UnitCostPerKg,
    [property: JsonRequired] string Reason);

/// <summary>The wire form is the name ("Approve" / "Reject").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InventoryOpeningCostDecisionKindV1>))]
public enum InventoryOpeningCostDecisionKindV1
{
    Approve = 1,
    Reject = 2,
}

/// <summary>The approver's decision on one pending submission, with the approver's own note.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideInventoryOpeningCostV1(
    [property: JsonRequired] InventoryOpeningCostDecisionKindV1 Decision,
    [property: JsonRequired] string Reason);

/// <summary>
/// The decision on a submission: <c>Approved</c> with the stock on hand it costed (canonical kg text), or
/// <c>Rejected</c> with none. Actor ids stay in the audit.
/// </summary>
public sealed record InventoryOpeningCostDecisionViewV1(
    string Decision,
    DateTimeOffset DecidedAtUtc,
    string Reason,
    string? CostedStockKg);

/// <summary>
/// One opening-cost submission: the position, the canonical unit cost text, the reason and the status —
/// <c>PendingApproval</c>, <c>Approved</c> or <c>Rejected</c>.
/// </summary>
public sealed record InventoryOpeningCostViewV1(
    Guid OpeningCostId,
    Guid CompanyId,
    Guid LocationId,
    Guid ProductId,
    string UnitCostPerKg,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string Status,
    InventoryOpeningCostDecisionViewV1? Decision);

public sealed record InventoryOpeningCostResultV1(
    InventoryOpeningCostViewV1 OpeningCost,
    bool IsReplay,
    string OriginalCorrelationId);

public static class InventoryOpeningCostStatusesV1
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
public interface IInventoryOpeningCostWriterV1
{
    Task<InventoryOpeningCostResultV1> SubmitOpeningCostAsync(
        SubmitInventoryOpeningCostV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryOpeningCostResultV1> DecideOpeningCostAsync(
        Guid openingCostId,
        DecideInventoryOpeningCostV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The fenced user command: submit needs <see cref="InventoryPermissionManifestV1.OpeningCostSubmit"/>, a decision
/// <see cref="InventoryPermissionManifestV1.OpeningCostApprove"/>, each measured company-wide and fresh before the
/// writer's single transaction. The caller supplies only the transport envelope (correlation, causation,
/// Idempotency-Key, channel); actor, permission and role codes are derived from the measured scope.
/// </summary>
public interface IInventoryOpeningCostCommandV1
{
    Task<InventoryOpeningCostResultV1> SubmitAsync(
        SubmitInventoryOpeningCostV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryOpeningCostResultV1> DecideAsync(
        Guid openingCostId,
        DecideInventoryOpeningCostV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

public static class InventoryOpeningCostCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string NotFound = "inventory_opening_cost_not_found";

    /// <summary>The submission id is already stored with other content.</summary>
    public const string Conflict = "inventory_opening_cost_conflict";

    /// <summary>The submission was already decided with another decision or note.</summary>
    public const string AlreadyDecided = "inventory_opening_cost_already_decided";

    /// <summary>Maker≠checker (SAT-D304 (1)): the submitter cannot decide their own submission.</summary>
    public const string SelfApproval = "inventory_opening_cost_self_approval";

    /// <summary>
    /// The cost ledger already holds a state for the position (known or unreadable); an opening cost never overwrites
    /// it. Whether and how such a position may be re-costed is not decided.
    /// </summary>
    public const string LedgerAlreadyTracked = "inventory_opening_cost_ledger_already_tracked";

    /// <summary>The company holds no own stock of the product at the location, so there is nothing to cost.</summary>
    public const string NoCostedStock = "inventory_opening_cost_no_costed_stock";

    /// <summary>Another submission for the same location and product is still waiting for a decision.</summary>
    public const string PendingExists = "inventory_opening_cost_pending_exists";

    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string Unavailable = "inventory_opening_cost_unavailable";
}

/// <summary>
/// The CLOSED problem codes of the two routes, per HTTP status. 401/403 are platform problems. Anything else the
/// writer raises is an internal fault, never a guessed 4xx.
/// </summary>
public static class InventoryOpeningCostHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = Set(InventoryOpeningCostCodesV1.ValidationFailed);

    public static IReadOnlySet<string> DecideStatus404Codes { get; } = Set(InventoryOpeningCostCodesV1.NotFound);

    public static IReadOnlySet<string> SubmitStatus409Codes { get; } = Set(
        InventoryOpeningCostCodesV1.IdempotencyConflict,
        InventoryOpeningCostCodesV1.Conflict,
        InventoryOpeningCostCodesV1.LedgerAlreadyTracked,
        InventoryOpeningCostCodesV1.NoCostedStock,
        InventoryOpeningCostCodesV1.PendingExists,
        InventoryOpeningCostCodesV1.ConcurrentChange);

    public static IReadOnlySet<string> DecideStatus409Codes { get; } = Set(
        InventoryOpeningCostCodesV1.IdempotencyConflict,
        InventoryOpeningCostCodesV1.AlreadyDecided,
        InventoryOpeningCostCodesV1.SelfApproval,
        InventoryOpeningCostCodesV1.LedgerAlreadyTracked,
        InventoryOpeningCostCodesV1.NoCostedStock,
        InventoryOpeningCostCodesV1.ConcurrentChange);

    /// <summary>Setup or stored-state faults, never the caller's input.</summary>
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        InventoryOpeningCostCodesV1.PermissionUnregistered,
        InventoryOpeningCostCodesV1.Unavailable);

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}

public sealed class InventoryOpeningCostException(
    int status,
    string code,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
