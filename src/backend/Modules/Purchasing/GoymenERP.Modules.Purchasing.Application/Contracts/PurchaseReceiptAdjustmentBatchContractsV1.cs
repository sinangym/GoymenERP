namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// ALC-S6 / ALIS_F017 sections3.7/3.8: all receipt corrections are evaluated as one net owner operation,
/// grouped by purchase general line. The host holds Sales locks before Purchase locks and owns the transaction.
/// This participant never commits. Inventory's cumulative preflight/staging is a separate owner operation.
/// </summary>
public interface IPurchaseReceiptAdjustmentBatchParticipantV1 : IAsyncDisposable
{
    /// <summary>Missing acceptances are omitted; the host compares exact requested identities before any mutation.</summary>
    Task<IReadOnlyList<PurchaseReceiptAdjustmentTargetV1>> ReadTargetsAsync(Guid orderId,
        IReadOnlyList<Guid> acceptanceIds, CancellationToken cancellationToken = default);

    /// <summary>Immutable batch replay precedes current versions, target reads and allocation budgets.</summary>
    Task<PurchaseReceiptAdjustmentBatchResultV1?> FindBatchReplayAsync(string commandName, string requestHash,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default);

    Task<PurchaseReceiptAdjustmentBatchResultV1> StageBatchAsync(PurchaseReceiptAdjustmentBatchStageV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default);

    Task<PurchaseReceiptAdjustmentSupplyV1?> ReadSupplyAsync(Guid orderId, Guid generalLineId,
        CancellationToken cancellationToken = default);
}

public sealed record PurchaseReceiptAdjustmentBatchStageV1(Guid CompanyId, Guid BatchId, Guid OrderId,
    string ExpectedOrderVersion, string Reason, DateTimeOffset OccurredAtUtc, string CommandName,
    string RequestHash, IReadOnlyList<PurchaseReceiptAdjustmentBatchLineV1> Lines);

/// <summary>Purchase-unit quantities; physical kg and Inventory movement identities remain Inventory's contract.</summary>
public sealed record PurchaseReceiptAdjustmentBatchLineV1(Guid AdjustmentId, Guid AcceptanceId, string UndoQuantity,
    bool ReplacementExpected, bool IsSupplierReturn, Guid? NewAcceptanceId = null, string? NewAcceptedQuantity = null);

public sealed record PurchaseReceiptAdjustmentBatchResultV1(Guid CompanyId, Guid BatchId, Guid OrderId,
    string OrderVersion, IReadOnlyList<PurchaseReceiptAdjustmentResultV1> Lines,
    IReadOnlyList<PurchaseReceiptAdjustmentBatchSupplyV1> Supplies, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// Authoritative final facts for ONE general line after all corrections. Consumers use these facts rather than
/// adding per-correction observations. NetOpenedQuantity excludes pre-existing supply and replacement receipts.
/// Every line result in the batch carries the same final order version.
/// </summary>
public sealed record PurchaseReceiptAdjustmentBatchSupplyV1(Guid GeneralLineId, string NetOpenedQuantity,
    string AllocatableQuantity, bool Revived);
