namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// ALC-S6 / ALS-D86/D91/D92/D93: Purchasing owns acceptance net shares and revival history.
/// A host enlists this single-use participant in its open transaction, locks every Sales demand
/// before every purchase general line, and owns commit/rollback. Inventory is a separate owner.
/// </summary>
public interface IPurchaseReceiptAdjustmentParticipantV1 : IAsyncDisposable
{
    Task<PurchaseReceiptAdjustmentTargetV1?> ReadTargetAsync(Guid orderId, Guid acceptanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replay precedes mutable target/version/cap reads; the owner checks actor, company and request hash.
    /// Success and terminal refusal share the Purchasing.Attempts command/key uniqueness. A recorded success
    /// is decoded from its immutable adjustment response, never restored from today's order. A stored refusal
    /// throws PurchasingServiceException with IsReplay=true and the attempt's original correlation id.
    /// </summary>
    Task<PurchaseReceiptAdjustmentResultV1?> FindReplayAsync(string commandName, string requestHash,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>Writes and flushes only Purchasing state; the borrowed transaction is never committed here.</summary>
    Task<PurchaseReceiptAdjustmentResultV1> StageAsync(PurchaseReceiptAdjustmentStageV1 request,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>Reads the post-write owner state on the SAME connection/transaction, including a closed line's zero supply.</summary>
    Task<PurchaseReceiptAdjustmentSupplyV1?> ReadSupplyAsync(Guid orderId, Guid generalLineId,
        CancellationToken cancellationToken = default);
}

public sealed record PurchaseReceiptAdjustmentTargetV1(Guid CompanyId, Guid OrderId, Guid AcceptanceId,
    Guid LineId, Guid GeneralLineId, Guid ProductId, string ProductVersion, Guid UnitId, bool UnitIsKilogram,
    string ObservedOrderVersion, string OriginalAcceptedQuantity, string AlreadyUndoneQuantity);

/// <summary>
/// UndoQuantity is in the original acceptance's purchase unit. A correction optionally includes its
/// replacement acceptance in the same net operation; a supplier return never supplies replacement receipt facts.
/// ReplacementExpected selects only the ALS-D91 supply effect and is immutable once recorded.
/// </summary>
public sealed record PurchaseReceiptAdjustmentStageV1(Guid CompanyId, Guid AdjustmentId, Guid OrderId,
    Guid AcceptanceId, string ExpectedOrderVersion, string UndoQuantity, bool ReplacementExpected,
    bool IsSupplierReturn, string Reason, DateTimeOffset OccurredAtUtc, string CommandName, string RequestHash,
    Guid? NewAcceptanceId = null, string? NewAcceptedQuantity = null,
    IReadOnlyList<PurchaseRevivalNotificationSnapshotV1>? NotificationSnapshots = null);

public sealed record PurchaseReceiptAdjustmentResultV1(Guid CompanyId, Guid AdjustmentId, Guid OrderId,
    Guid AcceptanceId, Guid LineId, Guid GeneralLineId, string OrderVersion, string NetOpenedQuantity,
    string AllocatableQuantity, bool Revived, bool IsReplay, string OriginalCorrelationId,
    Guid? NewAcceptanceId = null, string NewAcceptedQuantity = "0");

public sealed record PurchaseReceiptAdjustmentSupplyV1(Guid CompanyId, Guid OrderId, Guid GeneralLineId,
    string OrderVersion, bool IsAllocatable, string AllocatableQuantity);

public static class PurchaseReceiptAdjustmentCodesV1
{
    public const string CommandName = "purchasing.receipt-adjustment.stage.v1";
    public const string Unavailable = "purchasing_receipt_adjustment_unavailable";
    public const string UndoExceedsAcceptance = "purchasing_receipt_adjustment_exceeds_acceptance";
    public const string OriginalAcceptanceNotFound = "purchasing_receipt_adjustment_acceptance_not_found";
}
