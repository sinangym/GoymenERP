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
    long? currentVersion = null)
    : Exception(code)
{
    public ManufacturingApiStatus Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
    public long? ExpectedVersion { get; } = expectedVersion;
    public long? CurrentVersion { get; } = currentVersion;
}
