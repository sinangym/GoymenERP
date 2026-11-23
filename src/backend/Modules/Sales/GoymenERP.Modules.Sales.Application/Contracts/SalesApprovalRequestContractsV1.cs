using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// WP4 — the Sales approval requests (SINAN_KARARLARI_2026-11-22_EK_SORULAR SAT-D475 / SAT-D476, E-61 / E-62):
//  * SAT-D476: the salesperson opens a conversion request for an expired open quote; the request waits in the Work inbox; the
//    quote's own expired-conversion approval (SAT-D24 / SAT-D44, unchanged) answers it, after which the conversion is made. An
//    expired quote without a request is never counted as waiting.
//  * SAT-D475: a shipment above the order's approval-band tolerance creates an over-delivery request (who, when, how much, which
//    order line) through the Sales port the Logistics shipment gate calls; the request waits in the Work inbox from that moment;
//    the over-delivery approver (sales.orders.over-delivery.approve) decides; the shipment does not become definite before the
//    approval. Logistics never writes a Sales table: it calls ISalesOverDeliveryGatePortV1.
// No new permission is registered: the quote write opens a conversion request, the expired-conversion approval rejects one, the
// over-delivery approval decides an over-delivery request.

/// <summary>The versioned command names the approval request attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesApprovalRequestCommands
{
    /// <summary>POST /api/v1/sales/quotes/{id}/expired-conversion-requests (SAT-D476; <c>sales.quotes.write</c>).</summary>
    public const string RequestQuoteConversionV1 = "sales.approval-request.request-quote-conversion.v1";

    /// <summary>POST /api/v1/sales/quotes/{id}/expired-conversion-requests/{no}/rejection (<c>sales.quotes.expired-conversion.approve</c>).</summary>
    public const string RejectQuoteConversionV1 = "sales.approval-request.reject-quote-conversion.v1";

    /// <summary>POST /api/v1/sales/drafts/{id}/over-delivery-requests/{no}/approval (SAT-D475; <c>sales.orders.over-delivery.approve</c>).</summary>
    public const string ApproveOverDeliveryV1 = "sales.approval-request.approve-over-delivery.v1";

    /// <summary>POST /api/v1/sales/drafts/{id}/over-delivery-requests/{no}/rejection (a reason).</summary>
    public const string RejectOverDeliveryV1 = "sales.approval-request.reject-over-delivery.v1";

    public static bool IsApprovalRequestCommand(string? commandName) =>
        commandName is RequestQuoteConversionV1 or RejectQuoteConversionV1 or ApproveOverDeliveryV1 or RejectOverDeliveryV1;
}

/// <summary>Problem codes of the approval request commands.</summary>
public static class SalesApprovalRequestProblemCodesV1
{
    /// <summary>A pending conversion request of the same quote revision exists (409, not recorded).</summary>
    public const string Duplicate = "sales_approval_request_duplicate";

    /// <summary>The quote is not expired on the server's business day: no request is needed (409, recorded).</summary>
    public const string QuoteNotExpired = "sales_approval_request_quote_not_expired";

    /// <summary>The quote's current revision already carries its expired-conversion approval (409, recorded).</summary>
    public const string QuoteAlreadyApproved = "sales_approval_request_quote_already_approved";

    /// <summary>SAT-D216: the shipper decides their own over-delivery request (409, recorded).</summary>
    public const string SelfDecision = "sales_approval_request_self_decision";

    /// <summary>
    /// The over-delivery request no longer stands as asked: the order line's measured quantity changed (a revision), the order is
    /// terminal, or the shipped total it names is no longer in the approval band (409, recorded).
    /// </summary>
    public const string Stale = "sales_approval_request_stale";

    /// <summary>The over-delivery request's shipped total is in the block band: never approvable (422, not recorded).</summary>
    public const string Blocked = "sales_approval_request_blocked";

    /// <summary>A rejection without a reason (422, not recorded).</summary>
    public const string ReasonRequired = "sales_approval_request_reason_required";

    /// <summary>A reason or note above its bound (422, not recorded).</summary>
    public const string TextTooLong = "sales_approval_request_text_too_long";
}

/// <summary>The request body of the quote conversion request: the quote version the user saw and an optional note.</summary>
public sealed record SalesQuoteConversionRequestRequest(string ExpectedVersion, string? Note = null);

/// <summary>
/// One approval request as the screens and the inbox read it. <see cref="Status"/> is the derived answer: <c>Pending</c>,
/// <c>Approved</c>, <c>Rejected</c>, or <c>Stale</c> (a quote request whose quote moved on — revised, lost or converted — without
/// an approval of the asked revision). Quantities are exact invariant texts in the order unit; null on a quote request.
/// </summary>
public sealed record SalesApprovalRequestView(
    string Kind,
    Guid SubjectId,
    int RequestNo,
    string SubjectRevision,
    string Status,
    string Version,
    Guid? LineId,
    string? Ordered,
    string? Quantity,
    string? RequestedTotal,
    Guid? LoadingInstructionId,
    Guid? ShipmentId,
    string? Note,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

public sealed record SalesApprovalRequestListResponse(IReadOnlyList<SalesApprovalRequestView> Items);

public sealed record SalesApprovalRequestCommandResult(SalesApprovalRequestView Request, bool IsReplay, string CorrelationId);

/// <summary>The wire names of <see cref="SalesApprovalRequestView.Status"/> and <see cref="SalesApprovalRequestView.Kind"/>.</summary>
public static class SalesApprovalRequestApiV1
{
    public const string KindQuoteExpiredConversion = "QuoteExpiredConversion";
    public const string KindOverDelivery = "OverDelivery";
    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";
    public const string StatusStale = "Stale";

    /// <summary>The technical bound of one subject's listed requests (newest first).</summary>
    public const int MaximumListed = 50;

    public static string KindText(SalesApprovalRequestKind kind) => kind switch
    {
        SalesApprovalRequestKind.QuoteExpiredConversion => KindQuoteExpiredConversion,
        SalesApprovalRequestKind.OverDelivery => KindOverDelivery,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>The strict wire readers of the approval request commands (unknown members are refused by the shared options).</summary>
public static class SalesApprovalRequestJson
{
    public static SalesQuoteConversionRequestRequest ReadQuoteRequest(string json)
    {
        var request = JsonSerializer.Deserialize<SalesQuoteConversionRequestRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        return request.ExpectedVersion is null ? throw new JsonException("The expected version is required.") : request;
    }

    /// <summary>The approval carries the expected version only.</summary>
    public static SalesDraftStateCommandRequest ReadApproval(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: false);

    /// <summary>The rejection takes the expected version and a reason.</summary>
    public static SalesDraftStateCommandRequest ReadRejection(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: true);
}

public static class SalesApprovalRequestHash
{
    public static string Compute(Guid quoteId, SalesQuoteConversionRequestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesApprovalRequestCommands.RequestQuoteConversionV1,
            quoteId,
            request.ExpectedVersion,
            request.Note,
        }, SalesDraftJson.Options));
    }

    public static string Compute(string commandName, Guid subjectId, int requestNo, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = commandName,
            subjectId,
            requestNo,
            request.ExpectedVersion,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

public sealed record SalesApprovalRequestStoreResult(SalesApprovalRequest Request, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>What an approval request command key already holds: the stored request or the recorded terminal refusal, never both.</summary>
public sealed record SalesApprovalRequestReplay(SalesApprovalRequest? Request, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>
/// The shipment gate's request input as Sales records it (SAT-D475): the order line, the judged measured quantity and its
/// revision, the attempted shipment, the shipped total it would make and the Logistics references, in the shipper's name with the
/// shipper's write metadata (audit). Opened without an attempt key: at most one pending request per order line (the unique
/// pending index) is the idempotency.
/// </summary>
public sealed record SalesOverDeliveryRequestOpenV1(
    Guid OrderId,
    Guid LineId,
    int BasisRevision,
    decimal Ordered,
    decimal Quantity,
    decimal RequestedTotal,
    Guid LoadingInstructionId,
    Guid ShipmentId,
    SalesDraftWriteEnvelope Envelope);

/// <summary>
/// The approval request half of the Sales store (ADR-011 §2–§8 in its own attempt table): one save writes the request (or its
/// decision), the attempt and the audit event; an over-delivery approval also appends the order's own over-delivery approval fact
/// and the order's version step in the same save. The unique pending indexes refuse a second pending request of the same quote
/// revision or order line as the save's concurrent change. Another company's rows are invisible.
/// </summary>
public interface ISalesApprovalRequestStore
{
    Task<SalesApprovalRequestReplay?> FindApprovalRequestReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default);

    /// <summary>Stores a quote conversion request built with the quote's next request number, its attempt and its audit event.</summary>
    Task<SalesApprovalRequestStoreResult> StoreQuoteConversionRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid quoteId, Func<int, DateTimeOffset, SalesApprovalRequest> build, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>Records a terminal refusal under the key (bound to the quote or order).</summary>
    Task RecordApprovalRequestRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid subjectId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>A rejection: the request's one decision, its attempt and its audit event (null when the request does not exist).</summary>
    Task<SalesApprovalRequestStoreResult?> RejectApprovalRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        Action<SalesApprovalRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// SAT-D475: the over-delivery approval. <paramref name="beforeWrite"/> reads the order's judgement afresh on the order the
    /// store holds and answers the approval input; <paramref name="mutate"/> appends the order's over-delivery approval and decides
    /// the request. One save (order approval row + order version + request decision + attempt + audit). Null when the request or
    /// its order does not exist.
    /// </summary>
    Task<SalesApprovalRequestStoreResult?> ApproveOverDeliveryRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid orderId, int requestNo,
        Func<SalesOrderDraft, SalesApprovalRequest, CancellationToken, Task<OverDeliveryApprovalInput>> beforeWrite,
        Func<SalesOrderDraft, SalesApprovalRequest, OverDeliveryApprovalInput, DateTimeOffset, OverDeliveryApprovalFacts> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// SAT-D475: opens the over-delivery request of the gate (the order line's pending request is answered when one exists, nothing
    /// new is written then). Returns the pending request and whether it was created now.
    /// </summary>
    Task<(SalesApprovalRequest Request, bool Created)> OpenOverDeliveryRequestAsync(SalesOverDeliveryRequestOpenV1 input,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequest?> FindApprovalRequestAsync(SalesApprovalRequestKind kind, Guid subjectId, int requestNo,
        CancellationToken cancellationToken = default);

    /// <summary>The subject's requests of the kind, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SalesApprovalRequest>> ListApprovalRequestsAsync(SalesApprovalRequestKind kind, Guid subjectId, int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>The approval request commands and reads (SAT-D475 / SAT-D476).</summary>
public interface ISalesApprovalRequestService
{
    Task<SalesApprovalRequestCommandResult> RequestQuoteConversionAsync(Guid quoteId, SalesQuoteConversionRequestRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequestCommandResult> RejectQuoteConversionRequestAsync(Guid quoteId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequestListResponse> GetQuoteConversionRequestsAsync(Guid quoteId, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequestCommandResult> ApproveOverDeliveryRequestAsync(Guid draftId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequestCommandResult> RejectOverDeliveryRequestAsync(Guid draftId, int requestNo,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesApprovalRequestListResponse> GetOverDeliveryRequestsAsync(Guid draftId, CancellationToken cancellationToken = default);
}

// ---- the Logistics shipment gate port (SAT-D475) ---------------------------------------------------------------------------

/// <summary>The gate's answer. The default value is <see cref="Unavailable"/>: an answer nobody filled in is never a pass.</summary>
public enum SalesOverDeliveryGateResultV1
{
    /// <summary>Not decidable (an unknown order or line, an unreadable tolerance): the shipment stops (fail-closed).</summary>
    Unavailable = 0,

    /// <summary>Within the tolerance, in the warn band, or covered by a recorded over-delivery approval: the shipment passes.</summary>
    Pass = 1,

    /// <summary>In the approval band without a covering approval: the request is pending; the shipment stops until it is approved.</summary>
    Held = 2,

    /// <summary>In the block band: never approvable; the shipment stops.</summary>
    Blocked = 3,
}

/// <summary>
/// The gate question: the order line, the shipped total before this shipment (the delivery owner's own sum) and the attempted
/// quantity, both exact in the order unit, with the Logistics references and the shipper's write metadata (the request is opened
/// in the shipper's name; SAT-D216 keeps the shipper from deciding it).
/// </summary>
public sealed record SalesOverDeliveryGateRequestV1(
    Guid OrderId,
    Guid LineId,
    Guid LoadingInstructionId,
    Guid ShipmentId,
    decimal ShippedBefore,
    decimal Quantity,
    SalesDraftWriteEnvelope Envelope);

/// <summary>The gate answer: the result, the pending request's number (Held only) and the contract version.</summary>
public sealed record SalesOverDeliveryGateReadV1(
    Guid OrderId,
    Guid LineId,
    SalesOverDeliveryGateResultV1 Result,
    int? RequestNo,
    string ContractVersion);

/// <summary>The stable identifiers of the gate contract.</summary>
public static class SalesOverDeliveryGateContractV1
{
    public const string Version = "sales.over-delivery-gate.v1";
}

/// <summary>
/// SAT-D475: the versioned port the Logistics shipment gate calls before a definite shipment (owner Sales, consumer Logistics).
/// It judges the shipped total the shipment would make with the order's measured quantity, the Settings tolerance of the order's
/// business date and the one realization calculator; in the approval band without a covering approval it opens (or answers the
/// pending) over-delivery request — the only write, in Sales's own table, in the shipper's name — and answers Held. The consumer
/// measures its own authority first; Sales measures no Sales permission here. Without a resolved company it throws; any other
/// failure answers <see cref="SalesOverDeliveryGateResultV1.Unavailable"/>.
/// </summary>
public interface ISalesOverDeliveryGatePortV1
{
    Task<SalesOverDeliveryGateReadV1> EvaluateAsync(SalesOverDeliveryGateRequestV1 request, CancellationToken cancellationToken = default);
}
