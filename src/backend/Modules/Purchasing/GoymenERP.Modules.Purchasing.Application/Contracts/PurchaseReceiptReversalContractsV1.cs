using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>API-44 measured kg and original purchase-unit undo quantity are distinct facts; no receipt-tolerance ratio is inferred.</summary>
public sealed record ReversePurchaseReceiptV1(string ExpectedPurchaseOrderVersion, Guid AdjustmentId, Guid ReversalLineId,
    Guid InventoryMovementId, string UndoQuantity, string MeasuredUndoQuantityKg, string SourceBlock, string Reason,
    DateTimeOffset OccurredAtUtc, RecordPurchaseGoodsReceiptV1? ReplacementReceipt = null);

public sealed record PurchaseReceiptReversalConversionV1(Guid ConversionId, Guid FutureAllocationId,
    string UndoneQuantity, string UndoneKg, string ReopenedQuantity, string OverflowReleasedQuantity, string UnheldQuantity);

public sealed record PurchaseReceiptReversalStockV1(Guid CompanyId, Guid AdjustmentId, Guid AcceptanceId,
    Guid InventoryMovementId, Guid OriginalReceiptMovementId, Guid LabelId, string LabelVersion, string QuantityKg,
    IReadOnlyList<PurchaseReceiptReversalConversionV1> Conversions, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseReceiptCorrectionStockV1(Guid AcceptanceId, Guid LabelId, string LabelVersion,
    Guid MovementId, string AcceptedQuantity, string QuantityKg, IReadOnlyList<Guid> ReconsumedFutureAllocationIds,
    IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1> FutureSupplyReleases);

public sealed record PurchaseReceiptReversalResponseV1(PurchaseReceiptAdjustmentResultV1 Purchase,
    PurchaseReceiptReversalStockV1 Stock, PurchaseReceiptCorrectionStockV1? CorrectionStock);

/// <summary>The service has freshly authorized this owner operation. The host rechecks that SAME authority after awaited reads and before commit.</summary>
public sealed record PurchaseReceiptReversalWriteV1(Guid OrderId, Guid AcceptanceId, ReversePurchaseReceiptV1 Request,
    bool IsSupplierReturn = false, bool ReplacementExpected = false, string? OriginalUnitCostTryPerKg = null,
    string CommandName = PurchaseReceiptReversalApiV1.CommandName, string? RequestHash = null,
    PurchaseSupplierReturnCostAllocationV1? CostAllocation = null);

public interface IPurchaseReceiptReversalTransactionV1
{
    Task<PurchaseReceiptReversalResponseV1> ExecuteAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority,
        CancellationToken cancellationToken = default);

    /// <summary>The owner callback is invoked once after both adjustment stages, before the sole commit. Any failure rolls back all owners.</summary>
    Task<T> ExecuteWithWriteAsync<T>(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        PurchasingWriteEnvelope envelope, Func<PurchaseReceiptReversalResponseV1, CancellationToken, Task<T>> writeReturn,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default) =>
        Task.FromException<T>(new PurchasingServiceException(503, PurchaseReceiptAdjustmentCodesV1.Unavailable));
}

public delegate Task<IPurchaseReceiptAdjustmentParticipantV1> PurchaseReceiptAdjustmentParticipantFactoryV1(
    DbConnection connection, DbTransaction transaction, Guid companyId, CancellationToken cancellationToken);

public interface IPurchaseReceiptReversalServiceV1
{
    Task<PurchaseReceiptReversalResponseV1> ReverseGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
        ReversePurchaseReceiptV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public static class PurchaseReceiptReversalApiV1
{
    public const string ReversePath = PurchasingApiV1.OrderDetailPath + "/goods-receipts/{acceptanceId}/reversal";
    public const string CommandName = "purchasing.goods-receipt.reverse.v1";
}

public static class PurchaseReceiptReversalHashV1
{
    public static string Compute(PurchaseReceiptReversalWriteV1 write) => PurchasingRequestHash.Compute(
        PurchaseReceiptReversalApiV1.CommandName, write.OrderId,
        write.CostAllocation is null
            ? (object)new { write.AcceptanceId, write.Request, write.IsSupplierReturn, write.ReplacementExpected, write.OriginalUnitCostTryPerKg }
            : new { write.AcceptanceId, write.Request, write.IsSupplierReturn, write.ReplacementExpected, write.OriginalUnitCostTryPerKg, write.CostAllocation });
}

public static class PurchaseReceiptReversalJsonV1
{
    public static ReversePurchaseReceiptV1 Read(string json)
    {
        var request = JsonSerializer.Deserialize<ReversePurchaseReceiptV1>(json, PurchasingJson.Options) ?? throw new JsonException();
        Validate(request);
        return request;
    }

    public static void Validate(ReversePurchaseReceiptV1 request)
    {
        if (!long.TryParse(request.ExpectedPurchaseOrderVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < 1 || version.ToString(CultureInfo.InvariantCulture) != request.ExpectedPurchaseOrderVersion
            || request.AdjustmentId == Guid.Empty || request.ReversalLineId == Guid.Empty || request.InventoryMovementId == Guid.Empty
            || !Positive(request.UndoQuantity) || !Positive(request.MeasuredUndoQuantityKg)
            || request.SourceBlock is not ("Free" or "Blocked") || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > 1000 || request.Reason != request.Reason.Trim()
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero) throw new JsonException();
        if (request.ReplacementReceipt is { } next)
        {
            PurchaseGoodsReceiptJsonV1.Validate(next);
            if (next.ExpectedPurchaseOrderVersion != request.ExpectedPurchaseOrderVersion
                || next.MovementId == request.InventoryMovementId)
                throw new JsonException();
        }
    }

    private static bool Positive(string? text) => text is not null && text.Length <= 19
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && value > 0m && value < 1_000_000_000_000_000m
        && value.ToString("0.###", CultureInfo.InvariantCulture) == text;
}
