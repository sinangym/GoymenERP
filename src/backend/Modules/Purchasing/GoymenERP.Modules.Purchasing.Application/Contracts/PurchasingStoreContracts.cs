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
    public const string OrderCreateV1 = "purchasing.purchase-order.create.v1";
    public const string OrderApproveV1 = "purchasing.purchase-order.approve.v1";

    public static bool IsRequestCommand(string commandName) => commandName is RequestCreateV1 or RequestSubmitV1;

    public static bool IsOrderCommand(string commandName) => commandName is OrderCreateV1 or OrderApproveV1;

    public static bool IsMutation(string commandName) => commandName is RequestSubmitV1 or OrderApproveV1;

    public static bool IsKnown(string commandName) => IsRequestCommand(commandName) || IsOrderCommand(commandName);
}

/// <summary>The stable refusal codes of the store, beside the domain's rule codes.</summary>
public static class PurchasingStoreCodes
{
    public const string CompanyContextMismatch = "purchasing_company_context_mismatch";
    public const string SameKeyPayloadMismatch = "purchasing_same_key_payload_mismatch";
    public const string ConcurrentChange = "purchasing_concurrent_change";
    public const string ValueDoesNotFit = "purchasing_value_does_not_fit";
    public const string InvalidCursor = "purchasing_invalid_cursor";

    /// <summary>
    /// The Σ-shares rule (Sinan #66 5738113623 §1), measured by the store inside the approval's save: the
    /// ordered quantities of the APPROVED orders' general lines bound to one request line, plus this order's,
    /// exceed that request line's quantity. Field errors name the lines. The domain code
    /// <c>PurchasingRuleCodes.RequestLineOverSourced</c> is the same rule's name.
    /// </summary>
    public const string RequestLineOverSourced = "purchasing_request_line_over_sourced";
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

public sealed record PurchasingRequestPage(IReadOnlyList<StoredPurchaseRequest> Items, string? NextCursor);

public sealed record PurchasingOrderPage(IReadOnlyList<StoredPurchaseOrder> Items, string? NextCursor);

/// <summary>The audit facts of one transition (submit / approve): the versioned event type, the operation and the reason.</summary>
public sealed record PurchasingMutationAudit(string EventType, string Operation, string Reason);

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
    /// One transition (submit) of the company's stored request in ONE save: the domain transition, the attempt
    /// record and the audit event. The caller answers the domain's refusals (terminal ones through
    /// <see cref="RecordRefusalAsync"/>); another company's request, or none, is null (never 403); a same-key
    /// attempt already stored answers Replayed; a lost race is <see cref="PurchasingStoreCodes.ConcurrentChange"/> with nothing stored.
    /// </summary>
    Task<PurchasingRequestStoreResult?> MutateRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid requestId, Action<PurchaseRequest, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default);

    Task<StoredPurchaseRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>The company's requests, newest first, by opaque company-bound cursor; <paramref name="status"/> narrows to one status.</summary>
    Task<PurchasingRequestPage> ListRequestsAsync(int pageSize, string? cursor, PurchaseRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a new order the caller built (Draft, ≥1 general line, no acceptance), or answers the same attempt again. Draft orders reserve nothing (ASSUMPTION-PUR4-2).</summary>
    Task<PurchasingOrderStoreResult> StoreOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid companyAccountId, DateOnly effectiveOn,
        Func<PurchaseOrder> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One transition (approve) of the company's stored order in ONE save, as <see cref="MutateRequestAsync"/>.
    /// For an order opened for a request the Σ-shares rule is measured inside the same save
    /// (<see cref="PurchasingStoreCodes.RequestLineOverSourced"/>, with field errors) and the source request's
    /// sourcing version moves with the approval, so two approvals against the same request cannot both pass the
    /// sum unseen: the loser is <see cref="PurchasingStoreCodes.ConcurrentChange"/> and measures again.
    /// </summary>
    Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default);

    Task<StoredPurchaseOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The company's orders, newest first, by opaque company-bound cursor; <paramref name="status"/> narrows to one status.</summary>
    Task<PurchasingOrderPage> ListOrdersAsync(int pageSize, string? cursor, PurchaseOrderStatus? status = null,
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
