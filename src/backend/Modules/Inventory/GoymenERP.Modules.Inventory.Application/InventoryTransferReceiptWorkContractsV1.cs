namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Read the actual single-transfer context for one explicitly selected existing receipt command purpose.</summary>
public interface IInventoryTransferReceiptWorkV1
{
    Task<InventoryTransferReceiptWorkV1> ReadAsync(Guid transferId, string purpose, CancellationToken cancellationToken = default);
    Task<InventoryTransferReceiptWorkCandidatesV1> SearchAsync(string purpose, string? query = null,
        string? cursor = null, int limit = 25, CancellationToken cancellationToken = default);
}
public sealed record InventoryTransferReceiptWorkV1(string Purpose, InventoryTransferViewV1 Transfer,
    InventoryTransferReceiptFactsViewV1 Facts);
public sealed record InventoryTransferReceiptWorkCandidateV1(InventoryTransferListItemV1 Transfer, IReadOnlyList<string> LabelCodes);
public sealed record InventoryTransferReceiptWorkCandidatesV1(Guid CompanyId, string Purpose, string? Query, int Limit,
    IReadOnlyList<InventoryTransferReceiptWorkCandidateV1> Items, bool SearchWindowFull, string? NextCursor);
public sealed record InventoryTransferReceiptWorkDisplayCandidateV1(InventoryTransferListItemV1 Transfer, IReadOnlyList<string> LabelCodes,
    string SourceLocationCode, string SourceLocationName, string DestinationLocationCode, string DestinationLocationName);
public sealed record InventoryTransferReceiptWorkDisplayCandidatesV1(Guid CompanyId, string Purpose, string? Query, int Limit,
    IReadOnlyList<InventoryTransferReceiptWorkDisplayCandidateV1> Items, bool SearchWindowFull, string? NextCursor);
public interface IInventoryTransferReceiptWorkCursorV1
{
    bool IsAvailable { get; }
    string Protect(Guid companyId, string actorId, string purpose, string? query, int limit, Guid afterTransferId);
    Guid Unprotect(Guid companyId, string actorId, string purpose, string? query, int limit, string cursor);
}

public static class InventoryTransferReceiptWorkPurposesV1
{
    public const string Dispatch = "dispatch";
    public const string Receive = "receive";
    public const string OverageDecision = "overage-decision";
    public const string LossRequest = "loss-request";
    public const string LossDecision = "loss-decision";
    public static string? Permission(string? purpose) => purpose switch
    {
        Dispatch => InventoryPermissionManifestV1.StockTransferWrite,
        Receive => InventoryPermissionManifestV1.StockTransferWrite,
        OverageDecision => InventoryPermissionManifestV1.StockTransferOverageApprove,
        LossRequest => InventoryPermissionManifestV1.StockTransferLossRecord,
        LossDecision => InventoryPermissionManifestV1.StockTransferLossApprove,
        _ => null,
    };
}
