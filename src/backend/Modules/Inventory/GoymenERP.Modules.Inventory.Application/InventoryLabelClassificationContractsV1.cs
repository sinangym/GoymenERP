using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

[JsonConverter(typeof(JsonStringEnumConverter<InventoryLabelClassificationV1>))]
public enum InventoryLabelClassificationV1
{
    Standard = 1,
    SecondQuality = 2,
    Scrap = 3,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClassifyInventoryLabelV1(
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] InventoryLabelClassificationV1 Classification);

public sealed record InventoryLabelClassificationViewV1(
    Guid LabelId,
    string LabelVersion,
    string Classification,
    DateTimeOffset ClassifiedAtUtc);

public sealed record InventoryLabelClassificationResultV1(
    InventoryLabelClassificationViewV1 Classification,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The central host supplies the reviewed permission name. Inventory consumes it but does not
/// register, grant or infer a role from it.
/// </summary>
public sealed record InventoryLabelClassificationAuthorityV1(string PermissionCode);

public static class InventoryLabelClassificationCodesV1
{
    public const string Unauthenticated = "inventory_label_classification_unauthenticated";
    public const string Forbidden = "inventory_label_classification_forbidden";
    public const string PermissionUnregistered = "inventory_label_classification_permission_unregistered";
    public const string ValidationFailed = "inventory_label_classification_validation_failed";
    public const string IdempotencyConflict = "inventory_label_classification_idempotency_conflict";
    public const string LabelNotFound = "inventory_label_classification_label_not_found";
    public const string LabelVersionConflict = "inventory_label_classification_label_version_conflict";
    public const string ClassificationUnchanged = "inventory_label_classification_unchanged";
    public const string ConcurrentChange = "inventory_label_classification_concurrent_change";
}

public sealed class InventoryLabelClassificationException(int status, string code, Exception? inner = null)
    : Exception(code, inner)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public interface IInventoryLabelClassificationWriterV1
{
    Task<InventoryLabelClassificationResultV1> ClassifyAsync(
        Guid labelId,
        ClassifyInventoryLabelV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

public interface IInventoryLabelClassificationCommandV1
{
    Task<InventoryLabelClassificationResultV1> ClassifyAsync(
        Guid labelId,
        ClassifyInventoryLabelV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}
