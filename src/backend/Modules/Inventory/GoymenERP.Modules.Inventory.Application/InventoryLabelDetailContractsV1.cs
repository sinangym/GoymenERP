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
/// One current physical label and its position totals from the same versioned capture. Genealogy is
/// deliberately absent until split/merge owns a stored lineage source (DEC-LBL-002/003).
/// </summary>
public sealed record InventoryLabelDetailV1(
    string LabelVersion,
    InventoryLabelViewV1 Identity,
    InventoryLabelBalancesV1 Balances);

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
