using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// ALC-S16 (F-027; KLN-D41, KLN-D43, KLN-D45): bulk goods receipt — one receipt of one order line holds many labels.
// Each label row is a label number + kg (typed or scanned). The total kg is compared with the line's remaining kg under the
// Ayarlar tolerance: inside = a warning the user acknowledges, then posted; outside or not set = an approver decides.
// The existing single-label receipt (PurchaseGoodsReceiptContractsV1) and its participants are reused per label, unchanged.

/// <summary>One label row of a bulk receipt. Ids are client-made (idempotent posting); kg is the measured fact, never a conversion.</summary>
public sealed record PurchaseGoodsReceiptBatchLabelInputV1(
    Guid AcceptanceId, Guid LotId, Guid LabelId, Guid MovementId,
    string? SupplierLabelNumber, string? LabelCode, string MeasuredQuantityKg, string? HeatNumber, string? CoilNumber);

/// <summary>
/// KLN-D43: the bulk receipt request. <see cref="VarianceAcknowledged"/> is the user's answer to the inside-tolerance warning;
/// it never lets an outside-tolerance difference pass without an approver.
/// </summary>
public sealed record SubmitPurchaseGoodsReceiptBatchV1(
    string ExpectedPurchaseOrderVersion, string ExpectedProductVersion, Guid BatchId, Guid LocationId,
    DateTimeOffset OccurredAtUtc, bool VarianceAcknowledged, IReadOnlyList<PurchaseGoodsReceiptBatchLabelInputV1> Labels);

/// <summary>An approval or a rejection of a pending bulk receipt; a rejection needs a reason.</summary>
public sealed record DecidePurchaseGoodsReceiptBatchV1(string ExpectedVersion, string? Reason);

public sealed record PurchaseGoodsReceiptBatchLabelViewV1(
    int LineNo, Guid AcceptanceId, Guid LotId, Guid LabelId, Guid MovementId, string LabelCode, string LabelCodeSource,
    string? SupplierLabelNumber, string MeasuredQuantityKg, string? HeatNumber, string? CoilNumber);

public sealed record PurchaseGoodsReceiptBatchViewV1(
    Guid CompanyId, Guid BatchId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, string Status, string Version,
    string ExpectedQuantityKg, string ReceivedQuantityKg, string DifferenceKg, string VarianceOutcome, string? TolerancePercent,
    bool VarianceAcknowledged, Guid LocationId, DateTimeOffset OccurredAtUtc, string PreparedBy, DateTimeOffset PreparedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason, IReadOnlyList<PurchaseGoodsReceiptBatchLabelViewV1> Labels);

public sealed record PurchaseGoodsReceiptBatchResponseV1(PurchaseGoodsReceiptBatchViewV1 Batch, bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseGoodsReceiptBatchDetailV1(PurchaseGoodsReceiptBatchViewV1 Batch);
public sealed record PurchaseGoodsReceiptBatchPageV1(IReadOnlyList<PurchaseGoodsReceiptBatchViewV1> Items, bool SearchWindowFull);

/// <summary>
/// KLN-D41: whether a label code is free. <see cref="Suggestion"/> is the first free code with the Ayarlar suffix format (null when
/// no format is set or none fits); the user confirms it, it is never applied silently. <see cref="StockChecked"/> is false when
/// Inventory's label register could not be asked, so only Purchasing's reservations were checked.
/// </summary>
public sealed record PurchaseGoodsReceiptLabelCodeAvailabilityV1(string Code, bool Available, string? Suggestion);
public sealed record PurchaseGoodsReceiptLabelCodeCheckV1(IReadOnlyList<PurchaseGoodsReceiptLabelCodeAvailabilityV1> Items, bool StockChecked);

/// <summary>KLN-D41: a bulk receipt label found by our code or by the supplier's original label number.</summary>
public sealed record PurchaseGoodsReceiptLabelMatchV1(
    Guid BatchId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, string BatchStatus, Guid LabelId, string LabelCode,
    string? SupplierLabelNumber, string MeasuredQuantityKg, DateTimeOffset OccurredAtUtc);
public sealed record PurchaseGoodsReceiptLabelSearchPageV1(IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1> Items, bool SearchWindowFull);

public interface IPurchaseGoodsReceiptBatchServiceV1
{
    Task<PurchaseGoodsReceiptBatchResponseV1> SubmitGoodsReceiptBatchAsync(Guid orderId, Guid lineId, SubmitPurchaseGoodsReceiptBatchV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptBatchResponseV1> ApproveGoodsReceiptBatchAsync(Guid orderId, Guid batchId, DecidePurchaseGoodsReceiptBatchV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptBatchResponseV1> RejectGoodsReceiptBatchAsync(Guid orderId, Guid batchId, DecidePurchaseGoodsReceiptBatchV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptBatchDetailV1> GetGoodsReceiptBatchAsync(Guid orderId, Guid batchId, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptBatchPageV1> GetGoodsReceiptBatchesAsync(Guid orderId, int limit = 50, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptLabelCodeCheckV1> CheckGoodsReceiptLabelCodesAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default);
    Task<PurchaseGoodsReceiptLabelSearchPageV1> SearchGoodsReceiptLabelsAsync(string text, int limit = 50, CancellationToken cancellationToken = default);
}

public static class PurchaseGoodsReceiptBatchApiV1
{
    public const string SubmitPath = PurchasingApiV1.OrderDetailPath + "/lines/{lineId}/goods-receipt-batches";
    public const string BatchesPath = PurchasingApiV1.OrderDetailPath + "/goods-receipt-batches";
    public const string BatchDetailPath = BatchesPath + "/{batchId}";
    public const string ApprovalPath = BatchDetailPath + "/approval";
    public const string RejectionPath = BatchDetailPath + "/rejection";
    public const string LabelCodeAvailabilityPath = "/api/v1/purchasing/goods-receipt-label-codes/availability";
    public const string LabelSearchPath = "/api/v1/purchasing/goods-receipt-labels";

    /// <summary>The largest number of labels in one bulk receipt (one request, one transaction).</summary>
    public const int MaximumLabels = 200;

    /// <summary>The largest number of codes one availability check answers.</summary>
    public const int MaximumCheckedCodes = 100;

    /// <summary>The shortest search text (a one-character scan over every label is refused).</summary>
    public const int MinimumSearchLength = 2;
}

public static class PurchaseGoodsReceiptBatchCommandsV1
{
    public const string SubmitV1 = "purchasing.goods-receipt-batch.submit.v1";
    public const string ApproveV1 = "purchasing.goods-receipt-batch.approve.v1";
    public const string RejectV1 = "purchasing.goods-receipt-batch.reject.v1";
}

public static class PurchaseGoodsReceiptBatchCodesV1
{
    /// <summary>The batch flow is not wired (no store, no transaction host); 503, nothing recorded.</summary>
    public const string Unavailable = "purchasing_goods_receipt_batch_unavailable";

    /// <summary>The Ayarlar receipt policy answered an unusable value (tolerance outside 0..100 or a malformed format); 503, fail-closed.</summary>
    public const string PolicyInvalid = "purchasing_goods_receipt_batch_policy_invalid";

    /// <summary>KLN-D43: the bulk kg comparison is defined for kg order lines only.</summary>
    public const string UnitUnsupported = "purchasing_goods_receipt_batch_unit_unsupported";

    /// <summary>The existing acceptance rule holds: the total never exceeds the line's remaining quantity.</summary>
    public const string ExceedsRemaining = "purchasing_goods_receipt_batch_exceeds_remaining";

    /// <summary>KLN-D45: the difference is inside the tolerance; the warning must be acknowledged before the receipt posts.</summary>
    public const string VarianceAcknowledgementRequired = "purchasing_goods_receipt_batch_variance_acknowledgement_required";

    /// <summary>KLN-D41: the label code is already used in the company; a suffix suggestion is read from the availability check.</summary>
    public const string LabelCodeConflict = "purchasing_goods_receipt_label_code_conflict";

    /// <summary>KLN-D41: no supplier number, no entered code and no generated-code format in Ayarlar.</summary>
    public const string LabelCodeRequired = "purchasing_goods_receipt_label_code_required";

    /// <summary>The same code, acceptance, lot, label or movement id twice in one batch.</summary>
    public const string DuplicateLabel = "purchasing_goods_receipt_batch_duplicate_label";

    /// <summary>The batch id is already used by another batch.</summary>
    public const string BatchIdTaken = "purchasing_goods_receipt_batch_id_taken";
}

/// <summary>
/// KLN-D41/KLN-D45: the Ayarlar values the bulk receipt reads. Every member is nullable: null means "not entered in Ayarlar".
/// A null tolerance sends every difference to an approver; a null suffix format gives no suggestion; a null generated-code format
/// means a label without a supplier number needs an entered code. Purchasing never picks a default.
/// </summary>
public sealed record PurchaseGoodsReceiptPolicyV1(decimal? KgTolerancePercent, string? LabelCodeSuffixFormat, string? GeneratedLabelCodeFormat)
{
    public static PurchaseGoodsReceiptPolicyV1 NotSet { get; } = new(null, null, null);
}

/// <summary>
/// The Ayarlar owner's read of the receipt policy for one company (draft port; the Settings provider is requested from the
/// Settings owner). Until it is registered the module's default answers <see cref="PurchaseGoodsReceiptPolicyV1.NotSet"/>.
/// </summary>
public interface IPurchaseGoodsReceiptPolicyReadPortV1
{
    Task<PurchaseGoodsReceiptPolicyV1> ReadAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>
/// Optional Inventory read: which of these label codes are already registered as stock labels in the company (draft port; requested
/// from the Inventory owner). Without it only Purchasing's own reservations are checked and the posting participant stays the
/// final guard.
/// </summary>
public interface IPurchaseLabelCodeRegisterReadPortV1
{
    Task<IReadOnlySet<string>> RegisteredAsync(Guid companyId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
}

/// <summary>
/// The bulk receipt host owns ONE transaction/connection for the batch row and every label. Each label borrows a FRESH pair of the
/// existing single-use receipt participants (<see cref="EnlistLabelAsync"/>), in label order; the order version moves by one per
/// label. beforeCommit runs after every stage, immediately before the sole commit; any throw rolls everything back.
/// </summary>
public interface IPurchaseGoodsReceiptBatchTransactionV1
{
    Task<T> ExecuteAsync<T>(Guid companyId, Func<IPurchaseGoodsReceiptBatchParticipantsV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
}

public interface IPurchaseGoodsReceiptBatchParticipantsV1
{
    /// <summary>Enlists the receipt participants of label <paramref name="labelIndex"/> (0-based) in the open transaction.</summary>
    Task<IPurchaseGoodsReceiptParticipantsV1> EnlistLabelAsync(int labelIndex, CancellationToken cancellationToken);
}

/// <summary>One stored label of a bulk receipt.</summary>
public sealed record PurchaseGoodsReceiptBatchLabelRecord(
    int LineNo, Guid AcceptanceId, Guid LotId, Guid LabelId, Guid MovementId, string LabelCode, GoodsReceiptLabelCodeSource CodeSource,
    long? GeneratedNumber, string? SupplierLabelNumber, decimal MeasuredQuantityKg, string? HeatNumber, string? CoilNumber);

/// <summary>A new bulk receipt as the service decided it (status, variance and tolerance snapshot included).</summary>
public sealed record PurchaseGoodsReceiptBatchDraft(
    Guid BatchId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid ProductId, string ProductVersion, Guid UnitId, Guid LocationId,
    DateTimeOffset OccurredAtUtc, PurchaseGoodsReceiptBatchStatus Status, decimal ExpectedQuantityKg, decimal ReceivedQuantityKg,
    GoodsReceiptKgVarianceOutcome VarianceOutcome, decimal? TolerancePercent, bool VarianceAcknowledged,
    IReadOnlyList<PurchaseGoodsReceiptBatchLabelRecord> Labels);

public sealed record StoredPurchaseGoodsReceiptBatch(
    Guid CompanyId, PurchaseGoodsReceiptBatchDraft Batch, long Version, string PreparedBy, DateTimeOffset PreparedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason);

/// <summary>A same-key attempt already stored: the batch it recorded, or its terminal refusal.</summary>
public sealed record PurchaseGoodsReceiptBatchReplay(Guid? BatchId, PurchasingTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>
/// The bulk receipt's own records (batch, labels, attempts, audit). Every write is ONE SaveChanges: the batch change, its attempt and its
/// audit event commit together or not at all, inside the host transaction when one is open. Another company's batch is null.
/// </summary>
public interface IPurchaseGoodsReceiptBatchStore
{
    Task<PurchaseGoodsReceiptBatchReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);
    Task<StoredPurchaseGoodsReceiptBatch?> FindAsync(Guid orderId, Guid batchId, CancellationToken cancellationToken = default);
    Task<bool> BatchIdExistsAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>> ListForOrderAsync(Guid orderId, int take, CancellationToken cancellationToken = default);

    /// <summary>The codes among <paramref name="codes"/> that an unreleased label of the company already holds.</summary>
    Task<IReadOnlySet<string>> TakenLabelCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);

    /// <summary>The unreleased codes of the company that start with <paramref name="prefix"/> (for a suffix suggestion), at most <paramref name="take"/>.</summary>
    Task<IReadOnlySet<string>> TakenLabelCodesStartingWithAsync(string prefix, int take, CancellationToken cancellationToken = default);

    /// <summary>The next generated-code number of the company (the largest one used + 1, released ones included).</summary>
    Task<long> NextGeneratedNumberAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1>> SearchLabelsAsync(string text, int take, CancellationToken cancellationToken = default);

    Task<StoredPurchaseGoodsReceiptBatch> SaveNewAsync(PurchaseGoodsReceiptBatchDraft draft, PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);
    Task<StoredPurchaseGoodsReceiptBatch> SaveDecisionAsync(Guid orderId, PurchaseGoodsReceiptBatch decided, long expectedVersion, PurchasingWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken = default);
    Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken = default);
}

public static class PurchaseGoodsReceiptBatchJsonV1
{
    private static readonly Regex Quantity = new("\\A(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{0,2}[1-9])?\\z", RegexOptions.CultureInvariant);

    public static SubmitPurchaseGoodsReceiptBatchV1 ReadSubmit(string json)
    {
        var request = JsonSerializer.Deserialize<SubmitPurchaseGoodsReceiptBatchV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Batch body is required.");
        ValidateSubmit(request);
        return request;
    }

    public static DecidePurchaseGoodsReceiptBatchV1 ReadDecision(string json)
    {
        var request = JsonSerializer.Deserialize<DecidePurchaseGoodsReceiptBatchV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Decision body is required.");
        ValidateDecision(request);
        return request;
    }

    public static void ValidateSubmit(SubmitPurchaseGoodsReceiptBatchV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Version(request.ExpectedPurchaseOrderVersion) || !Token(request.ExpectedProductVersion) || request.BatchId == Guid.Empty
            || request.LocationId == Guid.Empty || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero
            || request.Labels is null || request.Labels.Count is 0 or > PurchaseGoodsReceiptBatchApiV1.MaximumLabels)
        {
            throw new JsonException("Invalid batch shape; versions, identities, UTC time and 1..200 labels are required.");
        }

        foreach (var label in request.Labels)
        {
            if (label is null || label.AcceptanceId == Guid.Empty || label.LotId == Guid.Empty || label.LabelId == Guid.Empty
                || label.MovementId == Guid.Empty || !OptionalToken(label.SupplierLabelNumber) || !OptionalToken(label.LabelCode)
                || !OptionalToken(label.HeatNumber) || !OptionalToken(label.CoilNumber) || !Positive(label.MeasuredQuantityKg))
            {
                throw new JsonException("Invalid label row; identities, a positive exact kg and bounded texts are required.");
            }
        }
    }

    public static void ValidateDecision(DecidePurchaseGoodsReceiptBatchV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Version(request.ExpectedVersion)
            || request.Reason is not null && (request.Reason.Length > PurchaseGoodsReceiptBatch.MaximumReasonLength || request.Reason.Any(IsForbiddenControl)))
        {
            throw new JsonException("Invalid decision shape; a canonical version and a bounded reason are required.");
        }
    }

    /// <summary>True when the text is a bounded, trimmed label/heat/coil token.</summary>
    public static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= GoodsReceiptLabelCodes.MaximumCodeLength
        && value == value.Trim() && !value.Any(char.IsControl);

    private static bool OptionalToken(string? value) => value is null || Token(value);

    private static bool Positive(string? value) => value is not null && Quantity.IsMatch(value) && value != "0";

    private static bool Version(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version >= 1 && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool IsForbiddenControl(char value) => char.IsControl(value) && value is not ('\n' or '\r' or '\t');
}
