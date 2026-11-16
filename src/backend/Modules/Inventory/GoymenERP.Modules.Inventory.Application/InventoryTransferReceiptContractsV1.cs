using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-22c transfer receipt contract (KLN-D21/KLN-D23/KLN-D24). The receiver records the weighed kilograms of one
/// in-transit label; a shortage is an automatic separate fire movement, an excess waits for a different person's
/// approval, and a pending label may be closed as a transit loss only through a recorded request and a different
/// person's approval. Host routing and permission registration are separate contributions.
/// </summary>
public static class InventoryTransferReceiptApiV1
{
    public const string ReceivePathTemplate = "/api/v1/inventory/transfers/{transferId}/receive-lines/{lineId}";

    public const string OverageDecisionPathTemplate =
        "/api/v1/inventory/transfers/{transferId}/receive-lines/{lineId}/overage-decision";

    public const string LossRequestPathTemplate = "/api/v1/inventory/transfers/{transferId}/lines/{lineId}/loss-request";

    public const string LossDecisionPathTemplate = "/api/v1/inventory/transfers/{transferId}/lines/{lineId}/loss-decision";

    public const string ReceiptFactsPathTemplate = "/api/v1/inventory/transfers/{transferId}/receipt-facts";

    public const int MaximumTextLength = 500;

    /// <summary>The canonical free text, or null when it is empty, too long or carries a control character.</summary>
    public static string? NormalizeText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > MaximumTextLength
            || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }
}

/// <summary>
/// B-22c receipt of one in-transit line. <see cref="ReceivedKg"/> is the receiver's weighed kilograms (positive,
/// canonical decimal text); KLN-D21 makes it final. <see cref="OverageReason"/> is required exactly when the received
/// kilograms exceed the dispatched kilograms (KLN-D24) and must be absent otherwise.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceiveInventoryTransferLineV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset ReceivedAtUtc,
    [property: JsonRequired] InventoryTransferDocumentReferenceV1 IncomingDocument,
    [property: JsonRequired] string ReceivedKg,
    string? OverageReason);

[JsonConverter(typeof(JsonStringEnumConverter<InventoryTransferDecisionKindV1>))]
public enum InventoryTransferDecisionKindV1
{
    Approve = 1,
    Reject = 2,
}

/// <summary>
/// KLN-D24: the decision on one recorded receipt excess. The decider must not be the receiver. An approval moves the
/// excess into the received destination bucket; a rejection keeps it out of stock. One decision per receipt.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideInventoryTransferOverageV1(
    [property: JsonRequired] InventoryTransferDecisionKindV1 Decision,
    [property: JsonRequired] DateTimeOffset DecidedAtUtc,
    [property: JsonRequired] string Reason);

/// <summary>
/// KLN-D23 (c): the loss request of one pending (in-transit) line, with the explanation. KLN-D97: after a rejection the
/// rejected request stays as it is and a NEW request (the next <see cref="InventoryTransferLossViewV1.RequestNumber"/>)
/// may be opened for the same line; it runs maker≠approver again. A line carries at most one undecided request at a
/// time (the reversible implementation detail KLN-D97 leaves open).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestInventoryTransferLossV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset RequestedAtUtc,
    [property: JsonRequired] string Explanation);

/// <summary>
/// KLN-D23 (c): the decision on one recorded loss request. The decider must not be the requester. An approval writes
/// the line off (transfer version +1); a rejection leaves the line pending. <see cref="RequestNumber"/> names the
/// request decided (KLN-D97); absent, it is the line's first request, so a stale decision never lands on a newer
/// request opened after a rejection.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideInventoryTransferLossV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] InventoryTransferDecisionKindV1 Decision,
    [property: JsonRequired] DateTimeOffset DecidedAtUtc,
    [property: JsonRequired] string Reason,
    int? RequestNumber = null);

/// <summary>
/// One recorded receipt. Kilograms are canonical decimal text. <see cref="OverageStatus"/> is <c>None</c>,
/// <c>Pending</c>, <c>Approved</c> or <c>Rejected</c>; the decision fields are set only once decided.
/// </summary>
public sealed record InventoryTransferReceiptViewV1(
    Guid LineId,
    string DispatchedKg,
    string ReceivedKg,
    string ShortageKg,
    string OverageKg,
    Guid? ShortageMovementId,
    string? OverageReason,
    DateTimeOffset ReceivedAtUtc,
    string ReceivedBy,
    string OverageStatus,
    string? OverageDecisionReason,
    DateTimeOffset? OverageDecidedAtUtc,
    string? OverageDecidedBy,
    Guid? OverageMovementId);

/// <summary>
/// One recorded loss request. <see cref="Status"/> is <c>Pending</c>, <c>Approved</c> or <c>Rejected</c>.
/// <see cref="RequestNumber"/> counts the line's requests from 1 (KLN-D97: a rejected request is never changed; a new
/// request takes the next number).
/// </summary>
public sealed record InventoryTransferLossViewV1(
    Guid LineId,
    int RequestNumber,
    string Explanation,
    DateTimeOffset RequestedAtUtc,
    string RequestedBy,
    string Status,
    string? DecisionReason,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedBy,
    Guid? MovementId);

public sealed record InventoryTransferReceiptFactsViewV1(
    Guid TransferId,
    Guid CompanyId,
    string Version,
    string Status,
    IReadOnlyList<InventoryTransferReceiptViewV1> Receipts,
    IReadOnlyList<InventoryTransferLossViewV1> Losses);

/// <summary>The HTTP body of a B-22c command answer: the transfer view and the receipt facts after the command.</summary>
public sealed record InventoryTransferReceiptResponseV1(
    InventoryTransferViewV1 Transfer,
    InventoryTransferReceiptFactsViewV1 Facts);

public sealed record InventoryTransferReceiptResultV1(
    InventoryTransferViewV1 Transfer,
    InventoryTransferReceiptFactsViewV1 Facts,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The B-22c refusals beyond <see cref="InventoryTransferCodesV1"/>. Every refusal here is a 409 except
/// <see cref="OverageReasonRequired"/> and <see cref="OverageReasonNotAllowed"/> (422).
/// </summary>
public static class InventoryTransferReceiptCodesV1
{
    /// <summary>
    /// KLN-D91: a label dispatched in more than one block/lot bucket is received only with its full dispatched
    /// kilograms; a difference is refused and never split over the buckets. Widening this needs a separate decision.
    /// </summary>
    public const string MultiBlockFullReceiptOnly = "inventory_transfer_receipt_multi_block_full_only";
    public const string OverageReasonRequired = "inventory_transfer_overage_reason_required";
    public const string OverageReasonNotAllowed = "inventory_transfer_overage_reason_not_allowed";
    public const string OverageNotPending = "inventory_transfer_overage_not_pending";
    public const string LossPending = "inventory_transfer_loss_pending";
    /// <summary>
    /// The line already carries an undecided loss request (KLN-D97: one undecided request per line at a time), or the
    /// same request was already recorded with a different payload.
    /// </summary>
    public const string LossAlreadyRequested = "inventory_transfer_loss_already_requested";
    public const string AlreadyDecided = "inventory_transfer_already_decided";

    /// <summary>KLN-D23/KLN-D24 maker≠approver: the recorder of a receipt or loss request cannot decide it.</summary>
    public const string SelfApproval = "inventory_transfer_self_approval";
}

/// <summary>
/// Inventory's transactional B-22c write boundary. The central command adapter supplies a freshly authorized
/// company/actor envelope; the maker≠approver comparison uses the stored recorder and that envelope's actor.
/// </summary>
public interface IInventoryTransferReceiptWriterV1
{
    Task<InventoryTransferReceiptResultV1?> ReplayReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1?> ReplayDecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> DecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1?> ReplayRequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> RequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1?> ReplayDecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> DecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReceiptReaderV1
{
    Task<InventoryTransferReceiptFactsViewV1?> ReadReceiptFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReceiptCommandV1
{
    Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> DecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> RequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReceiptResultV1> DecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReceiptQueryV1
{
    Task<InventoryTransferReceiptFactsViewV1> GetReceiptFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default);
}
