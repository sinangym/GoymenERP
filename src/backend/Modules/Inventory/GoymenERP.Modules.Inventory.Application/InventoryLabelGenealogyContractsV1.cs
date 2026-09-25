using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>The wire form is the name ("Split" / "Merge"), as the genealogy read already answers it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InventoryLabelTransformationKindV1>))]
public enum InventoryLabelTransformationKindV1
{
    Split = 1,
    Merge = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryLabelTransformationSourceV1(
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryLabelTransformationOutputV1(
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string Code,
    [property: JsonRequired] string QuantityKg);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransformInventoryLabelsV1(
    [property: JsonRequired] Guid TransformationId,
    [property: JsonRequired] InventoryLabelTransformationKindV1 Kind,
    [property: JsonRequired] IReadOnlyList<InventoryLabelTransformationSourceV1> Sources,
    [property: JsonRequired] IReadOnlyList<InventoryLabelTransformationOutputV1> Outputs,
    [property: JsonRequired] DateTimeOffset OccurredAtUtc,
    [property: JsonRequired] string Reason);

public sealed record InventoryLabelGenealogyEdgeV1(
    Guid TransformationId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    Guid SourceLabelId,
    string SourceCode,
    string SourceLabelVersion,
    string SourceQuantityKg,
    Guid OutputLabelId,
    string OutputCode,
    string OutputLabelVersion,
    string OutputQuantityKg);

public sealed record InventoryLabelTransformationViewV1(
    Guid TransformationId,
    Guid CompanyId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    IReadOnlyList<Guid> SourceLabelIds,
    IReadOnlyList<Guid> OutputLabelIds);

public sealed record InventoryLabelTransformationResultV1(
    InventoryLabelTransformationViewV1 Transformation,
    bool IsReplay,
    string OriginalCorrelationId);

public sealed record InventoryLabelGenealogyV1(
    Guid LabelId,
    string LabelCode,
    IReadOnlyList<InventoryLabelGenealogyEdgeV1> Edges,
    bool WindowFull);

/// <summary>
/// Inventory-owned write boundary. The host supplies a freshly authorized command envelope; this
/// contract deliberately introduces no permission name, grant, route or cross-company visibility.
/// </summary>
public interface IInventoryLabelGenealogyWriterV1
{
    Task<InventoryLabelTransformationResultV1> TransformAsync(
        TransformInventoryLabelsV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public interface IInventoryLabelGenealogyReaderV1
{
    Task<InventoryLabelGenealogyV1?> ReadAsync(
        Guid labelId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public static class InventoryLabelGenealogyCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string LabelNotFound = "inventory_label_not_found";
    public const string LabelVersionConflict = "inventory_label_version_conflict";
    public const string SourcePositionConflict = "inventory_label_source_position_conflict";
    public const string ActiveAllocationConflict = "inventory_label_active_allocation_conflict";
    public const string LabelCodeTaken = "inventory_label_code_taken";
    public const string TransformationConflict = "inventory_label_transformation_conflict";
    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string CorruptState = "inventory_label_genealogy_unavailable";
}

public sealed class InventoryLabelGenealogyException(
    int status,
    string code,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
