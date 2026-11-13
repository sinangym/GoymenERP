using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// Manufacturing-owned bridge for one public input-issue command. The implementation owns the
/// transaction that records the Manufacturing fact and the matching Inventory movement. The
/// expected parent-label version is resolved from the order's frozen plan snapshot by the host;
/// callers do not provide or infer it.
/// </summary>
public interface IManufacturingInventoryInputIssuePortV1
{
    Task<ManufacturingInventoryInputIssuePortResultV1> IssueAsync(
        ManufacturingInventoryInputIssuePortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>
/// Manufacturing-owned bridge for public unused-input return and output-declaration commands.
/// Every physical identity and current expected version is explicit. The implementation validates
/// the values with their owners and commits the Manufacturing and Inventory effects atomically.
/// </summary>
public interface IManufacturingInventoryReturnOutputPortV1
{
    Task<ManufacturingInventoryInputReturnPortResultV1> ReturnAsync(
        ManufacturingInventoryInputReturnPortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<ManufacturingInventoryOutputPortResultV1> DeclareOutputAsync(
        ManufacturingInventoryOutputPortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed record ManufacturingInventoryInputIssuePortCommandV1(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid IssueId,
    Guid ParentLabelId,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

public sealed record ManufacturingInventoryInputReturnPortCommandV1(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid ReturnId,
    Guid ParentLabelId,
    long ExpectedParentLabelVersion,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

public sealed record ManufacturingInventoryOutputPortCommandV1(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid OutputId,
    Guid ParentLabelId,
    long ExpectedParentLabelVersion,
    OutputClass OutputClass,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelCode,
    string? HeatNumber,
    string? CoilNumber,
    IReadOnlyList<string> RequiredDocuments,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string InitialBlock,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

public sealed record ManufacturingInventoryInputIssuePortResultV1(
    long OrderVersion,
    long ParentLabelVersion,
    ManufacturingInventoryPositionSnapshotV1 ParentPosition,
    bool IsReplay);

/// <summary>
/// Inventory-owned physical context captured from the issued parent label. Manufacturing carries
/// the opaque party-kind names and identities without choosing or changing ownership.
/// </summary>
public sealed record ManufacturingInventoryPositionSnapshotV1(
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId);

public sealed record ManufacturingInventoryInputReturnPortResultV1(
    long OrderVersion,
    long ParentLabelVersion,
    bool IsReplay);

public sealed record ManufacturingInventoryOutputPortResultV1(
    long OrderVersion,
    long ParentLabelVersion,
    long LabelVersion,
    bool IsReplay);

/// <summary>
/// A refusal from an owner-backed realization port. Status is the existing Manufacturing API
/// status channel; Code is the stable owner or host code. The command service adds no policy.
/// </summary>
public sealed class ManufacturingInventoryRealizationPortException(
    ManufacturingApiStatus status,
    string code,
    string? field = null,
    long? expectedVersion = null,
    long? currentVersion = null,
    ManufacturingInventoryStockShortfallV1? stockShortfall = null)
    : Exception(code)
{
    public ManufacturingApiStatus Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
    public long? ExpectedVersion { get; } = expectedVersion;
    public long? CurrentVersion { get; } = currentVersion;

    /// <summary>
    /// SAT-D318: the refused operation and its place, present only on Inventory's physical-stock conflict and only
    /// when Inventory named the refused input issue. Its absence changes nothing: status and code stay the reason.
    /// </summary>
    public ManufacturingInventoryStockShortfallV1? StockShortfall { get; } =
        RequirePhysicalStockConflict(status, code, stockShortfall);

    private static ManufacturingInventoryStockShortfallV1? RequirePhysicalStockConflict(ManufacturingApiStatus status,
        string code, ManufacturingInventoryStockShortfallV1? stockShortfall) =>
        stockShortfall is null
        || (status == ManufacturingApiStatus.Conflict
            && string.Equals(code, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
                StringComparison.Ordinal))
            ? stockShortfall
            : throw new ArgumentException("A stock shortfall rides only on Inventory's physical-stock conflict.",
                nameof(stockShortfall));
}

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): no operation takes physical stock below zero, and a refusal says which
/// operation it stopped and where. Inventory owns every value; Manufacturing carries the opaque kind names and the
/// identities without interpreting them, as it does for <see cref="ManufacturingInventoryPositionSnapshotV1"/>.
/// <para>
/// Version 1 of this shape: a changed shape is a new type, never an edited one, and a refusal without it stays exactly
/// as it was. <see cref="AvailableKg"/> and <see cref="ReservedKg"/> stay in process for the later refused-attempt
/// record; the public API does not disclose them while the owner has not decided who may see them.
/// </para>
/// </summary>
public sealed record ManufacturingInventoryStockShortfallV1
{
    public ManufacturingInventoryStockShortfallV1(string movementKind, string sourceKind, Guid sourceId,
        Guid? sourceLineId, Guid labelId, Guid productId, Guid? locationId, Kilograms requestedKg,
        Kilograms availableKg, Kilograms? reservedKg)
    {
        MovementKind = RequireName(movementKind, nameof(movementKind));
        SourceKind = RequireName(sourceKind, nameof(sourceKind));
        SourceId = RequireIdentity(sourceId, nameof(sourceId));
        SourceLineId = sourceLineId is { } line ? RequireIdentity(line, nameof(sourceLineId)) : null;
        LabelId = RequireIdentity(labelId, nameof(labelId));
        ProductId = RequireIdentity(productId, nameof(productId));
        LocationId = locationId is { } location ? RequireIdentity(location, nameof(locationId)) : null;
        if (requestedKg.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedKg), "A refused operation always asks for a positive mass.");
        }

        RequestedKg = requestedKg;
        AvailableKg = availableKg;
        ReservedKg = reservedKg;
    }

    /// <summary>Inventory's name of the refused movement kind (for an input issue, <c>Issue</c>).</summary>
    public string MovementKind { get; }

    /// <summary>Inventory's source kind of the refused movement (for an input issue, its stable source name).</summary>
    public string SourceKind { get; }

    /// <summary>The source the refused movement answered (for an input issue, the order).</summary>
    public Guid SourceId { get; }

    /// <summary>The source line, when the movement answers one line (for an input issue, the issue fact).</summary>
    public Guid? SourceLineId { get; }

    /// <summary>The label the refused line would have taken below zero.</summary>
    public Guid LabelId { get; }

    /// <summary>The label's product.</summary>
    public Guid ProductId { get; }

    /// <summary>The stored location of that stock; absent when the label holds no stock anywhere.</summary>
    public Guid? LocationId { get; }

    /// <summary>What the refused line asked for.</summary>
    public Kilograms RequestedKg { get; }

    /// <summary>In process only: what the location held before the refused line.</summary>
    public Kilograms AvailableKg { get; }

    /// <summary>In process only: what other demands held there, when a reservation decided the refusal.</summary>
    public Kilograms? ReservedKg { get; }

    private static string RequireName(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length == value.Length
            ? value
            : throw new ArgumentException("A kind name is non-blank and trimmed.", parameterName);

    private static Guid RequireIdentity(Guid value, string parameterName) =>
        value != Guid.Empty ? value : throw new ArgumentException("An identity is never empty.", parameterName);
}

/// <summary>
/// Üretim U3-a (URT-D21, URT-D3, URT-D22; API-20/21): Manufacturing-owned bridge to Inventory's plan-input allocation
/// (Kalan Modüller B-01). The implementation owns one SQL transaction: it runs the Manufacturing write it is given
/// (plan approval, plan cancellation or order closure), then stages Inventory's allocation or remainder release in the
/// same transaction, and commits both or neither. A refusal from either module rolls both back.
/// </summary>
public interface IManufacturingPlanAllocationPortV1
{
    /// <summary>
    /// Runs <paramref name="approve"/>, then allocates every input of the approved plan. Each input is allocated at the
    /// label version <paramref name="labelVersions"/> names (the version read from Inventory at approval, URT-D3); the
    /// version Inventory returns is stored by Manufacturing for the order and its input issue.
    /// </summary>
    Task<T> ApproveAndAllocateAsync<T>(Guid planId, IReadOnlyDictionary<Guid, long> labelVersions,
        Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="write"/>, then releases every active allocation of <paramref name="planId"/>. A plan
    /// without an active allocation releases nothing and succeeds.
    /// </summary>
    Task<T> WriteAndReleaseAsync<T>(Guid planId, ManufacturingPlanAllocationReleaseReasonV1 reason,
        Func<CancellationToken, Task<T>> write, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>Why a plan's remaining allocation is released.</summary>
public enum ManufacturingPlanAllocationReleaseReasonV1
{
    PlanCancelled = 1,
    OrderClosed = 2,
}

/// <summary>One allocation Inventory wrote for a plan input (Manufacturing's own copy of the identity).</summary>
public sealed record ManufacturingPlanInputAllocationV1(
    Guid ParentRollId,
    Guid AllocationId,
    long LabelVersionAfter,
    Kilograms AllocatedKg);

/// <summary>Üretim U3-a: the plan-input allocation's own refusal codes (Inventory's codes pass through unchanged).</summary>
public static class ManufacturingPlanAllocationCodesV1
{
    /// <summary>The two module effects of one allocation did not come out of one transaction together.</summary>
    public const string Incomplete = "manufacturing_plan_allocation_incomplete";

    /// <summary>A plan input carries no label version to allocate at.</summary>
    public const string MissingLabelVersion = "manufacturing_plan_allocation_label_version_missing";

    /// <summary>Inventory no longer knows a planned parent label.</summary>
    public const string LabelNotFound = "manufacturing_plan_allocation_label_not_found";

    /// <summary>The planned parent label now carries another product than the plan's technical snapshot (URT-D3).</summary>
    public const string LabelIdentityChanged = "manufacturing_plan_allocation_label_identity_changed";
}
