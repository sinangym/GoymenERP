using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// API-07 — tek sevk risk istisnası, Satış tarafı (EK21 SAT-D153…D162; EK7 SAT-D47/D48/D53; EK31 SAT-D200; DEC-FIN-009;
// Sinan 2026-11-10, Issue #66 comment 6771235622 / EK55 pending). EK21 removes the date-range (customer) variant: only the
// single-shipment exception exists. Sales records the request, the decision and the validity; the Logistics shipment gate
// reads it later through ISalesShipmentRiskExceptionReadPortV1 (no Logistics connection is opened here, SAT-D209).

/// <summary>The versioned command names the risk exception attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesRiskExceptionCommands
{
    /// <summary>API-07: POST /api/v1/sales/drafts/{id}/risk-exceptions (SAT-D159).</summary>
    public const string RequestV1 = "sales.order.risk-exception.request.v1";

    /// <summary>API-07: POST /api/v1/sales/drafts/{id}/risk-exceptions/{no}/approval (SAT-D153).</summary>
    public const string ApproveV1 = "sales.order.risk-exception.approve.v1";

    /// <summary>API-07: POST /api/v1/sales/drafts/{id}/risk-exceptions/{no}/rejection (SAT-D153, a reason).</summary>
    public const string RejectV1 = "sales.order.risk-exception.reject.v1";

    public static bool IsRiskExceptionCommand(string? name) => name is RequestV1 or ApproveV1 or RejectV1;
}

/// <summary>Routes and technical bounds of API-07 (not policies).</summary>
public static class SalesRiskExceptionApiV1
{
    public const string RiskExceptionsPath = "/api/v1/sales/drafts/{id}/risk-exceptions";
    public const string ApprovalPath = "/api/v1/sales/drafts/{id}/risk-exceptions/{no}/approval";
    public const string RejectionPath = "/api/v1/sales/drafts/{id}/risk-exceptions/{no}/rejection";

    public const int CreatedStatus = 201;

    public const int CommandStatus = 200;

    /// <summary>Technical bound (not a policy): the most exceptions one order lists in one read (newest first).</summary>
    public const int MaximumListed = 200;
}

/// <summary>The stable problem codes of API-07 besides the shared draft codes.</summary>
public static class SalesRiskExceptionProblemCodesV1
{
    /// <summary>409 (recorded under the key): the requester decides their own request (SAT-D153/D159).</summary>
    public const string SelfDecision = SalesRuleCodes.RiskExceptionSelfDecision;

    /// <summary>409 (not recorded; the order can change): the order is not approved, or it is cancelled or closed.</summary>
    public const string OrderNotEligible = SalesRuleCodes.RiskExceptionOrderNotEligible;

    /// <summary>409 (not recorded; a decision can change it): the shipment already has a pending or a today-valid exception.</summary>
    public const string Duplicate = SalesRuleCodes.RiskExceptionDuplicate;
}

/// <summary>
/// API-07 request body: the single shipment (loading instruction id, SAT-D154) and the reason. The id is an opaque Logistics
/// reference; Sales neither reads nor writes the Logistics owner's tables (SAT-D209). NEEDS-SINAN: whether the shipment key is
/// the loading instruction, the dispatch note or the definite shipment.
/// </summary>
public sealed record RequestSalesRiskExceptionRequest(Guid LoadingInstructionId, string? Reason);

/// <summary>
/// One risk exception as the screens see it. No amount, limit or balance is ever answered (SAT-D162). <see cref="State"/> is
/// the stored axis (<c>Requested</c>, <c>Approved</c>, <c>Rejected</c>); <see cref="IsValidToday"/> says whether an approved
/// exception still releases its shipment on today's business date (SAT-D160).
/// </summary>
public sealed record SalesRiskExceptionView(
    Guid OrderId,
    int ExceptionNo,
    Guid LoadingInstructionId,
    string State,
    string Version,
    string Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason,
    DateOnly? ValidOn,
    bool IsValidToday);

public sealed record SalesRiskExceptionListResponse(IReadOnlyList<SalesRiskExceptionView> Items);

/// <summary>The command's answer to the route: the exception, whether it was replayed, and the first correlation (replay header).</summary>
public sealed record SalesRiskExceptionCommandResult(SalesRiskExceptionView Exception, bool IsReplay, string OriginalCorrelationId);

/// <summary>The strict reader of the request body: unknown members, a null body or an empty id is a 400 (never repaired).</summary>
public static class SalesRiskExceptionJson
{
    public static RequestSalesRiskExceptionRequest ReadRequest(string json)
    {
        var request = JsonSerializer.Deserialize<RequestSalesRiskExceptionRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (request.LoadingInstructionId == Guid.Empty)
        {
            throw new JsonException("Invalid loading instruction id.");
        }

        if (request.Reason is null)
        {
            throw new JsonException("A reason is required.");
        }

        return request;
    }

    /// <summary>The approval carries the expected version only (a reason is an unknown member).</summary>
    public static SalesDraftStateCommandRequest ReadApproval(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: false);

    /// <summary>The rejection takes the expected version and a reason.</summary>
    public static SalesDraftStateCommandRequest ReadRejection(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: true);
}

/// <summary>The canonical contents of the API-07 commands for the replay identity (ADR-011 §3).</summary>
public static class SalesRiskExceptionHash
{
    public static string Compute(Guid orderId, RequestSalesRiskExceptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesRiskExceptionCommands.RequestV1,
            orderId,
            request.LoadingInstructionId,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    public static string Compute(string commandName, Guid orderId, int exceptionNo, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = commandName,
            orderId,
            exceptionNo,
            request.ExpectedVersion,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>What the store did for an API-07 command: the exception, recorded now or replayed, and the first correlation.</summary>
public sealed record SalesRiskExceptionStoreResult(SalesShipmentRiskException Exception, SalesDraftWriteOutcome Outcome,
    string OriginalCorrelationId);

/// <summary>What an API-07 command key already holds: the stored exception or the recorded terminal refusal, never both.</summary>
public sealed record SalesRiskExceptionReplay(SalesShipmentRiskException? Exception, SalesDraftTerminalRefusal? Refusal,
    string OriginalCorrelationId);

/// <summary>
/// The risk exception half of the Sales store (the request/invoice attempt rules, ADR-011 §2–§8, in its own attempt table): one
/// save writes the exception, the attempt and the audit event. The unique pending index refuses a second pending exception of the
/// same shipment and the per-order number index a racing number, both as the save's concurrent change. Another company's
/// exceptions are invisible. Sales writes only its own tables (SAT-D209).
/// </summary>
public interface ISalesRiskExceptionStore
{
    /// <summary>The stored answer or recorded refusal of the key, null when the key is new; another content or actor is a 409.</summary>
    Task<SalesRiskExceptionReplay?> FindRiskExceptionReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new exception of the company's order in one save (the row, the attempt, the audit); <paramref name="build"/> gets
    /// the next per-order number and the commit instant. A racing same key answers the winner.
    /// </summary>
    Task<SalesRiskExceptionStoreResult> StoreRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid orderId,
        Func<int, DateTimeOffset, SalesShipmentRiskException> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>ADR-011 §7: records a terminal refusal of an API-07 command under its key.</summary>
    Task RecordRiskExceptionRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid orderId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>
    /// One decision on the company's stored exception in one save (the row, the attempt, the audit). <paramref name="mutate"/> is
    /// the domain's decision; a refusal writes nothing. Null when the exception does not exist here.
    /// </summary>
    Task<SalesRiskExceptionStoreResult?> DecideRiskExceptionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid orderId, int exceptionNo, Action<SalesShipmentRiskException, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    Task<SalesShipmentRiskException?> FindRiskExceptionAsync(Guid orderId, int exceptionNo, CancellationToken cancellationToken = default);

    /// <summary>The company's exceptions of the order, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SalesShipmentRiskException>> ListRiskExceptionsAsync(Guid orderId, int limit, CancellationToken cancellationToken = default);

    /// <summary>The company's exceptions of one shipment of the order (every state), newest first.</summary>
    Task<IReadOnlyList<SalesShipmentRiskException>> ListShipmentRiskExceptionsAsync(Guid orderId, Guid loadingInstructionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// API-07 (SAT-D153…D162): request, approve and reject a single-shipment risk exception, and list an order's exceptions. Company,
/// actor and authority are the server's and measured afresh before anything is read, after every awaited read and right before
/// the single save.
/// <list type="bullet">
/// <item>Request needs <c>sales.orders.risk-exception.request</c> only — never <c>sales.drafts.read</c> (SAT-D162).</item>
/// <item>Approve and reject need <c>sales.orders.risk-exception.approve</c> and the expected version; the requester never
/// decides their own request (SAT-D153/D159).</item>
/// <item>The list needs <c>sales.drafts.read</c>. No answer carries an amount, a limit or a balance (SAT-D162).</item>
/// </list>
/// </summary>
public interface ISalesRiskExceptionService
{
    Task<SalesRiskExceptionCommandResult> RequestRiskExceptionAsync(Guid draftId, RequestSalesRiskExceptionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesRiskExceptionCommandResult> ApproveRiskExceptionAsync(Guid draftId, int exceptionNo, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesRiskExceptionCommandResult> RejectRiskExceptionAsync(Guid draftId, int exceptionNo, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesRiskExceptionListResponse> GetRiskExceptionsAsync(Guid draftId, CancellationToken cancellationToken = default);
}

/// <summary>What the Sales side says about one shipment's risk exception on today's business date (SAT-D160).</summary>
public enum SalesShipmentRiskExceptionStatusV1
{
    /// <summary>No exception was ever requested for this shipment of this order.</summary>
    None = 0,

    /// <summary>A request waits for its decision; the shipment stays held.</summary>
    Pending = 1,

    /// <summary>Approved on today's business date: releases this one shipment (SAT-D154/D160).</summary>
    Valid = 2,

    /// <summary>Approved on an earlier business date: lapsed, a new approval is needed (SAT-D160).</summary>
    Lapsed = 3,

    /// <summary>Rejected; the shipment stays held.</summary>
    Rejected = 4,
}

/// <summary>
/// The read port answer: the status and, when one exists, the deciding exception's number and validity date. No amount, actor
/// or reason is part of it (SAT-D162).
/// </summary>
public sealed record SalesShipmentRiskExceptionReadV1(Guid OrderId, Guid LoadingInstructionId, SalesShipmentRiskExceptionStatusV1 Status,
    int? ExceptionNo, DateOnly? ValidOn);

/// <summary>
/// The versioned read port the Logistics shipment gate will consume (DEC-FIN-009, SAT-D154/D160). It reads under the caller's
/// server-resolved company (tenant filter) and creates or changes nothing; another company's order answers
/// <see cref="SalesShipmentRiskExceptionStatusV1.None"/>. Among several exceptions of the shipment a today-valid one wins, then a
/// pending one, then the newest. Without a resolved company or the Europe/Istanbul time zone it throws: the gate fails closed
/// (the shipment stays held). Consumption on a definite shipment and the cancellation lapse are the gate's contract, not yet
/// opened (NEEDS-SINAN).
/// </summary>
public interface ISalesShipmentRiskExceptionReadPortV1
{
    Task<SalesShipmentRiskExceptionReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken cancellationToken = default);
}
