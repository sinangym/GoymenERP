namespace GoymenERP.Modules.Inventory.Application;

public static class InventoryLabelDetailApiV1
{
    public const string DetailPath = "/api/v1/inventory/labels/{labelId:guid}/detail";
    public const string GetDetailOperationId = "GetInventoryLabelDetailV1";
}

public static class InventoryLabelDetailCodesV1
{
    public const string NotFound = "inventory_label_detail_not_found";
    public const string ReadConflict = "inventory_label_detail_read_conflict";
    public const string Unavailable = "inventory_label_detail_unavailable";
    public const string Unauthenticated = "inventory_label_detail_unauthenticated";
    public const string Forbidden = "inventory_label_detail_forbidden";
}

/// <summary>
/// One current physical label, its position totals and its stored split/merge lineage from the same
/// version-fenced capture (DEC-LBL-002/003, AT-038).
/// </summary>
public sealed record InventoryLabelDetailV1(
    string LabelVersion,
    InventoryLabelViewV1 Identity,
    InventoryLabelBalancesV1 Balances,
    InventoryLabelGenealogyV1 Genealogy,
    InventoryLabelMovementTimelineV1 MovementTimeline);

/// <summary>
/// The bounded, newest-first physical movement history for one label. Cost and cross-module
/// source details are deliberately not projected; the stable source identity is enough to audit
/// which accepted Inventory event changed the label.
/// </summary>
public sealed record InventoryLabelMovementTimelineV1(
    Guid LabelId,
    IReadOnlyList<InventoryLabelMovementV1> Entries,
    bool WindowFull);

public sealed record InventoryLabelMovementV1(
    Guid MovementId,
    string Kind,
    DateTimeOffset OccurredAtUtc,
    string SourceKind,
    Guid SourceId,
    Guid? SourceLineId,
    string SourceGrain,
    IReadOnlyList<InventoryLabelMovementLineV1> Lines);

public sealed record InventoryLabelMovementLineV1(
    int Sequence,
    string Direction,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string QuantityKg);

public interface IInventoryLabelDetailsV1
{
    Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default);
}

public sealed class InventoryLabelDetailException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
