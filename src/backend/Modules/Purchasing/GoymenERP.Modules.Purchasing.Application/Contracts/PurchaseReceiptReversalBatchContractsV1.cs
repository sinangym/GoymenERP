using System.Text.Json;
using System.Data.Common;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>API-44: one reasoned correction with one expected version; quantities remain in their original units.</summary>
public sealed record ReversePurchaseReceiptsV1(Guid BatchId, string ExpectedPurchaseOrderVersion, string Reason,
    DateTimeOffset OccurredAtUtc, IReadOnlyList<ReversePurchaseReceiptBatchLineV1> Lines);

public sealed record ReversePurchaseReceiptBatchLineV1(Guid AcceptanceId, Guid AdjustmentId, Guid ReversalLineId,
    Guid InventoryMovementId, string UndoQuantity, string MeasuredUndoQuantityKg, string SourceBlock,
    RecordPurchaseGoodsReceiptV1? ReplacementReceipt = null)
{
    public ReversePurchaseReceiptV1 SingleRequest(ReversePurchaseReceiptsV1 batch) => new(batch.ExpectedPurchaseOrderVersion,
        AdjustmentId, ReversalLineId, InventoryMovementId, UndoQuantity, MeasuredUndoQuantityKg, SourceBlock,
        batch.Reason, batch.OccurredAtUtc, ReplacementReceipt);
}

public sealed record PurchaseReceiptReversalBatchResponseV1(Guid CompanyId, Guid OrderId, Guid BatchId,
    string OrderVersion, IReadOnlyList<PurchaseReceiptReversalResponseV1> Lines,
    IReadOnlyList<PurchaseReceiptAdjustmentBatchSupplyV1> Supplies, bool Revived, bool IsReplay,
    string OriginalCorrelationId);

public delegate Task<IPurchaseReceiptAdjustmentBatchParticipantV1> PurchaseReceiptAdjustmentBatchParticipantFactoryV1(
    DbConnection connection, DbTransaction transaction, Guid companyId, CancellationToken cancellationToken);

public sealed record PurchaseReceiptReversalBatchWriteV1(Guid OrderId, ReversePurchaseReceiptsV1 Request);

public interface IPurchaseReceiptReversalBatchTransactionV1
{
    Task<PurchaseReceiptReversalBatchResponseV1> ExecuteBatchAsync(Guid companyId, PurchaseReceiptReversalBatchWriteV1 write,
        PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
}

public interface IPurchaseReceiptReversalBatchServiceV1
{
    Task<PurchaseReceiptReversalBatchResponseV1> ReverseGoodsReceiptsAsync(Guid orderId, ReversePurchaseReceiptsV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public static class PurchaseReceiptReversalBatchApiV1
{
    public const int MaximumLines = 100;
    public const string CommandName = "purchasing.goods-receipt.reverse-batch.v1";
    public const string Path = PurchasingApiV1.OrderDetailPath + "/goods-receipts/reversal-batch";
}

public static class PurchaseReceiptReversalBatchHashV1
{
    public static string Compute(PurchaseReceiptReversalBatchWriteV1 write) =>
        PurchasingRequestHash.Compute(PurchaseReceiptReversalBatchApiV1.CommandName, write.OrderId, write.Request);
}

public static class PurchaseReceiptReversalBatchJsonV1
{
    public static ReversePurchaseReceiptsV1 Read(string json)
    {
        var request = JsonSerializer.Deserialize<ReversePurchaseReceiptsV1>(json, PurchasingJson.Options) ?? throw new JsonException();
        Validate(request); return request;
    }

    public static void Validate(ReversePurchaseReceiptsV1 request)
    {
        if (request is null || request.BatchId == Guid.Empty || request.Lines is null
            || request.Lines.Count is < 1 or > PurchaseReceiptReversalBatchApiV1.MaximumLines)
            throw new JsonException();
        foreach (var line in request.Lines)
        {
            if (line is null || line.AcceptanceId == Guid.Empty || line.ReplacementReceipt?.AcceptanceId == line.AcceptanceId)
                throw new JsonException();
            PurchaseReceiptReversalJsonV1.Validate(line.SingleRequest(request));
        }
        if (request.Lines.Select(line => line.AcceptanceId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.AdjustmentId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.ReversalLineId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.InventoryMovementId).Distinct().Count() != request.Lines.Count)
            throw new JsonException();
        var newReceipts = request.Lines.Where(line => line.ReplacementReceipt is not null).Select(line => line.ReplacementReceipt!).ToArray();
        if (newReceipts.Select(line => line.AcceptanceId).Distinct().Count() != newReceipts.Length
            || newReceipts.Select(line => line.MovementId).Distinct().Count() != newReceipts.Length
            || newReceipts.Any(next => request.Lines.Any(line => line.AcceptanceId == next.AcceptanceId
                || line.InventoryMovementId == next.MovementId))) throw new JsonException();
    }
}
