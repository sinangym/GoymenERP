namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Stable owner declaration for the first company movement-timeline read (ST-04).</summary>
public static class InventoryMovementTimelineApiV1
{
    public const string Path = "/api/v1/inventory/movements";
    public const string GetOperationId = "GetInventoryMovementTimelineV1";
}

/// <summary>
/// Bounded newest-first Inventory history. It reports immutable physical movements only; it does not
/// infer reversals, enrich cross-module source facts, expose cost, or create a stock effect.
/// </summary>
public interface IInventoryMovementTimelineV1
{
    Task<InventoryMovementTimelinePageV1> SearchAsync(
        InventoryMovementTimelineQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryMovementTimelineQueryV1(int Limit = 50, string? Cursor = null);

public sealed record InventoryMovementTimelinePageV1(
    IReadOnlyList<InventoryMovementV1> Items,
    string? NextCursor);

public sealed record InventoryMovementV1(
    Guid MovementId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    string SourceGrain,
    IReadOnlyList<InventoryMovementLineV1> Lines);

public sealed record InventoryMovementLineV1(
    int Sequence,
    string Direction,
    Guid LabelId,
    string LabelCode,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string QuantityKg);

public static class InventoryMovementTimelineCodesV1
{
    public const string Unauthenticated = "inventory_movement_timeline_unauthenticated";
    public const string Forbidden = "inventory_movement_timeline_forbidden";
    public const string InvalidQuery = "inventory_movement_timeline_invalid_query";
    public const string Unavailable = "inventory_movement_timeline_unavailable";
}

public sealed class InventoryMovementTimelineException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
