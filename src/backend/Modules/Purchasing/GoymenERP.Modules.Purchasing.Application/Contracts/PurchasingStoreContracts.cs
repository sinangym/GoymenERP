using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The ADR-009 audit envelope of one purchasing write (the Sales / Logistics shape): who asked, under
/// which permission, in which conversation, with which client key. Every value is the caller's server-side
/// context; the company is always the server-resolved one.
/// </summary>
public sealed record PurchasingWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

public enum PurchasingWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The versioned command names the attempt record is keyed by (ADR-011 §2).</summary>
public static class PurchasingCommands
{
    public const string RequestCreateV1 = "purchasing.purchase-request.create.v1";
    public const string RequestSubmitV1 = "purchasing.purchase-request.submit.v1";

    /// <summary>A1-1 (ALS-D2): Submitted → Approved, under <c>purchasing.requests.approve</c>.</summary>
    public const string RequestApproveV1 = "purchasing.purchase-request.approve.v1";

    /// <summary>A1-1 (No.161 §1): Submitted | Approved → Rejected with a reason, under <c>purchasing.requests.approve</c>.</summary>
    public const string RequestRejectV1 = "purchasing.purchase-request.reject.v1";

    /// <summary>A1-1b (ALS-D46, ALS-D47): Submitted → Withdrawn with a reason, by the recorded preparer or submitter, under <c>purchasing.requests.withdraw</c>.</summary>
    public const string RequestWithdrawV1 = "purchasing.purchase-request.withdraw.v1";

    public const string OrderCreateV1 = "purchasing.purchase-order.create.v1";
    public const string OrderApproveV1 = "purchasing.purchase-order.approve.v1";

    /// <summary>A1-3 (plan API-10, No.161 §2): Approved → Cancelled — the whole unaccepted remainder, with a reason, under <c>purchasing.orders.cancel</c>.</summary>
    public const string OrderCancelV1 = "purchasing.purchase-order.cancel.v1";

    /// <summary>
    /// A3 API-30 (ALS-D33, ALS-D21): a Draft or Approved order's general line gets a new full delivery schedule (termin dilimleri,
    /// Σ = the line's ordered quantity), under <c>purchasing.orders.revise</c> (ALS-D94); the order's version moves by one.
    /// </summary>
    public const string OrderLineScheduleV1 = "purchasing.purchase-order.line-schedule.v1";

    /// <summary>
    /// API-11 (ALIS_F017 v1 §3.6, ALS-D10/D12/D87): an Approved order's general line closes its unaccepted remainder (eksik teslim
    /// kapanışı) with a required reason; writes the line closure event and the <c>purchasing.order-line.closed.v1</c> outbox message.
    /// </summary>
    public const string OrderLineCloseV1 = "purchasing.purchase-order.line-close.v1";

    /// <summary>API-11 reopening (ALS-D10): a closed general line reopens its closed share with a required reason.</summary>
    public const string OrderLineReopenV1 = "purchasing.purchase-order.line-reopen.v1";

    /// <summary>
    /// ALC-SC33 (FNS-D79/D80/D91): a Draft foreign-currency order's exchange rate is changed by its preparer before approval, with an
    /// optional reason; the order's version moves by one. Locked at approval (no revision with re-approval is built).
    /// </summary>
    public const string OrderExchangeRateChangeV1 = "purchasing.purchase-order.exchange-rate-change.v1";

    /// <summary>
    /// F-017 §3.5 F17-4 (ALS-D83): one Inventory future allocation on the order's general line is released with
    /// <c>purchasing-link-dropped</c>; the order and its quantities do not change (no version move), under
    /// <c>purchasing.future-allocation.release</c>.
    /// </summary>
    public const string OrderFutureAllocationReleaseV1 = "purchasing.purchase-order.future-allocation-release.v1";

    /// <summary>ALS-D85: the confirmed allocations are released with <c>source-cancelled</c> and the remainder is cancelled, in one transaction.</summary>
    public const string OrderCancelWithAllocationReleaseV1 = "purchasing.purchase-order.cancel-with-allocation-release.v1";

    /// <summary>F-017 §3.6: the confirmed allocations of the line are released with <c>purchase-line-closed</c> and the line is closed, in one transaction.</summary>
    public const string OrderLineCloseWithAllocationReleaseV1 = "purchasing.purchase-order.line-close-with-allocation-release.v1";

    public static bool IsRequestCommand(string commandName) =>
        commandName is RequestCreateV1 or RequestSubmitV1 or RequestApproveV1 or RequestRejectV1 or RequestWithdrawV1;

    public static bool IsOrderCommand(string commandName) =>
        commandName is OrderCreateV1 or OrderApproveV1 or OrderCancelV1 or OrderLineScheduleV1 or OrderLineCloseV1 or OrderLineReopenV1
            or OrderExchangeRateChangeV1 or OrderFutureAllocationReleaseV1 or OrderCancelWithAllocationReleaseV1
            or OrderLineCloseWithAllocationReleaseV1;

    public static bool IsRequestMutation(string commandName) =>
        commandName is RequestSubmitV1 or RequestApproveV1 or RequestRejectV1 or RequestWithdrawV1;

    public static bool IsOrderMutation(string commandName) =>
        commandName is OrderApproveV1 or OrderCancelV1 or OrderLineScheduleV1 or OrderLineCloseV1 or OrderLineReopenV1 or OrderExchangeRateChangeV1
            or OrderCancelWithAllocationReleaseV1 or OrderLineCloseWithAllocationReleaseV1;

    /// <summary>API-11: the line closure commands (all take the §3.4 line lock and write a closure event + outbox message).</summary>
    public static bool IsLineClosure(string commandName) =>
        commandName is OrderLineCloseV1 or OrderLineReopenV1 or OrderLineCloseWithAllocationReleaseV1;

    /// <summary>A1-3 / ALS-D85: the remainder cancellation commands (both take every general line lock of the order).</summary>
    public static bool IsOrderCancellation(string commandName) => commandName is OrderCancelV1 or OrderCancelWithAllocationReleaseV1;

    public static bool IsMutation(string commandName) => IsRequestMutation(commandName) || IsOrderMutation(commandName);

    public static bool IsKnown(string commandName) => IsRequestCommand(commandName) || IsOrderCommand(commandName)
        || commandName is PurchaseReceiptReversalApiV1.CommandName or PurchaseSupplierReturnCommandsV1.CreateV1
            or PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1;
}

/// <summary>F-017 F17-4: the JSON snapshots of the released allocation for the order's audit record (before / after).</summary>
public sealed record PurchasingLinkAuditSnapshots(string Before, string After);

/// <summary>F-017 F17-4: the order and what the read answered inside the same transaction.</summary>
public sealed record PurchasingOrderLinkRead<T>(StoredPurchaseOrder Order, T Value);

/// <summary>The stable refusal codes of the store, beside the domain's rule codes.</summary>
public static class PurchasingStoreCodes
{
    public const string CompanyContextMismatch = "purchasing_company_context_mismatch";
    public const string SameKeyPayloadMismatch = "purchasing_same_key_payload_mismatch";
    public const string ConcurrentChange = "purchasing_concurrent_change";
    public const string ValueDoesNotFit = "purchasing_value_does_not_fit";
    public const string InvalidCursor = "purchasing_invalid_cursor";

    /// <summary>
    /// F-017 F17-1 (§3.4): a purchase general line's serialization lock was not granted in time (another module's or Purchasing's
    /// write holds it); nothing was stored and the refusal is not terminal (the same key may try again).
    /// </summary>
    public const string LineBusy = "purchasing_line_busy";

    /// <summary>
    /// The Σ-shares rule (Sinan #66 5738113623 §1), measured by the store inside the approval's save: the
    /// ordered quantities of the APPROVED orders' general lines bound to one request line, plus this order's,
    /// exceed that request line's quantity. Field errors name the lines. The domain code
    /// <c>PurchasingRuleCodes.RequestLineOverSourced</c> is the same rule's name.
    /// </summary>
    public const string RequestLineOverSourced = "purchasing_request_line_over_sourced";

    /// <summary>
    /// A1-1 (ALS-D2, fail-closed): an order opened for a request is approved only while that request is APPROVED,
    /// measured inside the approval's save on the request row the approval also moves; a rejected (or not yet
    /// approved) request's draft order cannot become a commitment.
    /// </summary>
    public const string SourceRequestNotApproved = "purchasing_source_request_not_approved";

    /// <summary>F-027: a stored schedule revision does not read as one recording (mixed actor or time, or a gap in its slice order).</summary>
    public const string ScheduleHistoryInconsistent = "purchasing_schedule_history_inconsistent";
}

public sealed class PurchasingStoreException(string code, string message, Exception? innerException = null,
    IReadOnlyList<PurchasingFieldError>? errors = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
    public IReadOnlyList<PurchasingFieldError> Errors { get; } = errors ?? [];
}

/// <summary>A stored request with the business date its unit profiles were read for and the Product owner's answered version per line (kept beside the domain record, not in it).</summary>
public sealed record StoredPurchaseRequest(PurchaseRequest Request, DateOnly EffectiveOn, IReadOnlyDictionary<Guid, string> ProductVersionsByLineId);

/// <summary>A stored order, the same way, plus the supplier's Cari company account (the domain record names the party only).</summary>
public sealed record StoredPurchaseOrder(PurchaseOrder Order, Guid CompanyAccountId, DateOnly EffectiveOn,
    IReadOnlyDictionary<Guid, string> ProductVersionsByLineId);

public sealed record PurchasingRequestStoreResult(StoredPurchaseRequest Request, PurchasingWriteOutcome Outcome, string OriginalCorrelationId);

public sealed record PurchasingOrderStoreResult(StoredPurchaseOrder Order, PurchasingWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>ADR-011 §7: a terminal refusal recorded under the attempt key (stale version, state, Σ-shares), answered again on the same key.</summary>
public sealed record PurchasingTerminalRefusal(int Status, string Code, IReadOnlyList<PurchasingFieldError> Errors);

/// <summary>What the key holds: the stored request, the stored order or the recorded refusal — exactly one.</summary>
public sealed record PurchasingReplay(StoredPurchaseRequest? Request, StoredPurchaseOrder? Order, PurchasingTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>F-027: one stored delivery-schedule revision of a line — the order version it produced, its recorder and time, its slices.</summary>
public sealed record StoredScheduleRevision(long OrderVersionAfter, string RecordedBy, DateTimeOffset RecordedAtUtc,
    IReadOnlyList<StoredScheduleSlice> Slices);

/// <summary>F-027: one stored slice of a schedule revision, its quantity in the line's unit.</summary>
public sealed record StoredScheduleSlice(Guid SliceId, int SliceNo, decimal Quantity, DateOnly DeliveryDate);

public sealed record PurchasingRequestPage(IReadOnlyList<StoredPurchaseRequest> Items, string? NextCursor);

public sealed record PurchasingOrderPage(IReadOnlyList<StoredPurchaseOrder> Items, string? NextCursor);

/// <summary>The audit facts of one transition (submit / approve): the versioned event type, the operation and the reason.</summary>
public sealed record PurchasingMutationAudit(string EventType, string Operation, string Reason);

/// <summary>
/// A1-1: what the store measured about a request inside the transition's own save, for the domain to decide on —
/// whether an APPROVED order is bound to it (No.161 §1 refuses a rejection — and an A1-1b withdrawal — then).
/// </summary>
public sealed record PurchaseRequestBindingFacts(bool HasApprovedOrder);

/// <summary>
/// The persistent purchasing store of the server-resolved company (PUR-4a). One call is one save: the
/// record, its lines, the attempt record and the audit event commit together or not at all. Replay
/// identity (ADR-011, R3): (company, command, SHA-256 of the trimmed key); the raw key is never stored; the
/// same key with the same content and actor answers the stored result, another content or actor is one 409;
/// a key of another company is invisible. Nothing here is physical: an approved order is expected supply,
/// never stock (Inventory T09 owns the receipt).
/// </summary>
public interface IPurchasingStore
{
    /// <summary>Stores a new request the caller built (Draft, ≥1 line) with the Product owner's answered version per line, or answers the same attempt again.</summary>
    Task<PurchasingRequestStoreResult> StoreRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<PurchaseRequest> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One transition (submit, approve, reject, withdraw) of the company's stored request in ONE save: the domain transition,
    /// the attempt record and the audit event. The store measures the request's <see cref="PurchaseRequestBindingFacts"/>
    /// inside the same save and hands them to the domain; the request row is this write's concurrency token, and an
    /// order approval moves the same row, so a rejection and a binding approval cannot both pass unseen. The caller
    /// answers the domain's refusals (terminal ones through <see cref="RecordRefusalAsync"/>); another company's
    /// request, or none, is null (never 403); a same-key attempt already stored answers Replayed; a lost race is
    /// <see cref="PurchasingStoreCodes.ConcurrentChange"/> with nothing stored.
    /// </summary>
    Task<PurchasingRequestStoreResult?> MutateRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid requestId, Action<PurchaseRequest, PurchaseRequestBindingFacts, DateTimeOffset> mutate, PurchasingMutationAudit audit,
        CancellationToken cancellationToken = default);

    Task<StoredPurchaseRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>The company's requests, newest first, by opaque company-bound cursor; <paramref name="status"/> narrows to one status.</summary>
    Task<PurchasingRequestPage> ListRequestsAsync(int pageSize, string? cursor, PurchaseRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a new order the caller built (Draft, ≥1 general line, no acceptance), or answers the same attempt again. Draft orders reserve nothing (ASSUMPTION-PUR4-2).</summary>
    Task<PurchasingOrderStoreResult> StoreOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid companyAccountId, DateOnly effectiveOn,
        Func<PurchaseOrder> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One transition (approve, cancel, line schedule) of the company's stored order in ONE save, as <see cref="MutateRequestAsync"/>.
    /// For an order opened for a request the source request must be APPROVED
    /// (<see cref="PurchasingStoreCodes.SourceRequestNotApproved"/>, A1-1) and the Σ-shares rule is measured inside the same save
    /// (<see cref="PurchasingStoreCodes.RequestLineOverSourced"/>, with field errors) and the source request's
    /// sourcing version moves with the approval, so two approvals against the same request cannot both pass the
    /// sum unseen: the loser is <see cref="PurchasingStoreCodes.ConcurrentChange"/> and measures again.
    /// A1-3: the remainder cancellation is one more transition. A3 API-30: the line schedule command replaces exactly one line's
    /// delivery schedule and appends its slices as a revision at the new order version (the earlier revisions stay); the Σ-shares
    /// rule is measured only by the approval.
    /// </summary>
    Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// F-017 §3.5 F17-4 (ALS-D85, §3.6): <see cref="MutateOrderAsync"/> with a <paramref name="participant"/> that runs inside the
    /// same transaction, after the domain transition and its invariants and before the single save, while every general line lock
    /// of the order is held (cancellation and closure commands only). The participant gets the transitioned order and the
    /// transaction; a participant failure rolls everything back and nothing is written.
    /// </summary>
    Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> participant,
        PurchasingMutationAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// F-017 §3.5 F17-4 (ALS-D83): the link drop. Takes every general line lock of the company's order (ascending) in one transaction,
    /// runs <paramref name="participant"/> (which releases through the Inventory owner and answers the audit's before/after
    /// snapshots), and records the attempt and the order's audit record (version unchanged) in the same transaction. The order itself
    /// is not changed. A recorded attempt under the key answers the order again (replay); an unknown order is null.
    /// </summary>
    Task<PurchasingOrderStoreResult?> ReleaseOrderFutureAllocationAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid orderId,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task<PurchasingLinkAuditSnapshots>> participant, PurchasingMutationAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// F-017 §3.5 F17-4: reads the company's order and runs <paramref name="read"/> inside a short read transaction (no lock, nothing
    /// written) so the Inventory owner's read joins the same connection. An unknown order is null.
    /// </summary>
    Task<PurchasingOrderLinkRead<T>?> ReadOrderFutureAllocationsAsync<T>(Guid orderId,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default);

    Task<StoredPurchaseOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ALS-D94 / ALS-D81 (F-027): the company's recorded delivery-schedule revisions of one order line, oldest first — read only, as
    /// stored (append-only rows, one revision per order version that recorded it). Another company's order or line, or none, is an
    /// empty list (never 403). A revision whose slices disagree on who recorded it or when is
    /// <see cref="PurchasingStoreCodes.ScheduleHistoryInconsistent"/>.
    /// </summary>
    Task<IReadOnlyList<StoredScheduleRevision>> ListLineScheduleRevisionsAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default);

    /// <summary>The company's orders, newest first, by opaque company-bound cursor; status and supplier are optional exact filters.</summary>
    Task<PurchasingOrderPage> ListOrdersAsync(int pageSize, string? cursor, PurchaseOrderStatus? status = null, Guid? supplierPartyId = null,
        CancellationToken cancellationToken = default);

    Task<PurchasingReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken = default);
}

/// <summary>The canonical content of a command for the replay identity (R3): the strict wire shape, serialised and hashed.</summary>
public static class PurchasingRequestHash
{
    public static string Compute(string commandName, object request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = commandName, request }, PurchasingJson.Options));
    }

    public static string Compute(string commandName, Guid targetId, object request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = commandName, targetId, request }, PurchasingJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
