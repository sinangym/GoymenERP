using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// API-20 / API-21 (plan §3.2, S-01 "Satış Talebi / Teklif Havuzu"; delegation No 160, 161): the sales request wire contract.
// A request records a customer's demand — customer, business date, product lines with quantities — and its approval. It
// carries no price and no money profile. Every route lives under /api/v1/sales/requests; the commands use the draft
// routes' metadata (one canonical Idempotency-Key, the strict body, `expectedVersion` text) and answer the request after
// the command, the same key again answering 200/201 with the replay headers (ADR-011 §4).

public sealed record CreateSalesRequestRequest(
    Guid PartyId, Guid CompanyAccountId, DateOnly BusinessDate,
    IReadOnlyList<SalesRequestLineRequest> Lines,
    DateOnly? RequestedDeliveryDate = null, string? Note = null);

public sealed record SalesRequestLineRequest(
    Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid OrderUnitId, string Quantity,
    DateOnly? RequestedDeliveryDate = null, string? Note = null);

/// <summary>
/// The request in a list: its identity, the approval state (<c>Submitted</c>, <c>Approved</c> or <c>Rejected</c>), the
/// concurrency version as text, the customer and the business date.
/// </summary>
public sealed record SalesRequestSummaryView(
    Guid Id, Guid CompanyId, string DocumentNo, string State, string Version,
    Guid PartyId, Guid CompanyAccountId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, int LineCount);

public sealed record SalesRequestLineView(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion, string ProductCode, string ProductName,
    Guid OrderUnitId, string OrderUnitCode, string Quantity, DateOnly? RequestedDeliveryDate, string? Note);

/// <summary>The decision facts: who decided and when, and a rejection's reason (null on an approval).</summary>
public sealed record SalesRequestDecisionV1(string DecidedBy, DateTimeOffset DecidedAtUtc, string? RejectionReason);

public sealed record SalesRequestDetailView(
    SalesRequestSummaryView Request, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesRequestLineView> Lines, SalesRequestDecisionV1? Decision);

public sealed record SalesRequestResponse(SalesRequestDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record SalesRequestListResponse(IReadOnlyList<SalesRequestSummaryView> Items, string? NextCursor);

/// <param name="State">An exact state name (<c>Submitted</c>, <c>Approved</c>, <c>Rejected</c>) to narrow the list to, or null.</param>
public sealed record SalesRequestListQuery(int PageSize = 50, string? Cursor = null, string? State = null);

public static class SalesRequestApiV1
{
    public const string RequestPath = "/api/v1/sales/requests";
    public const string RequestDetailPath = "/api/v1/sales/requests/{id}";
    public const string ApprovalPath = "/api/v1/sales/requests/{id}/approval";
    public const string RejectionPath = "/api/v1/sales/requests/{id}/rejection";

    /// <summary>The create and its replay answer 201 (the draft create's ADR-011 §4 shape).</summary>
    public const int CreatedStatus = 201;

    /// <summary>The approval and the rejection answer the request after the decision with 200.</summary>
    public const int CommandStatus = 200;
}

/// <summary>The versioned problem codes of the sales request routes beyond the shared draft codes.</summary>
public static class SalesRequestProblemCodesV1
{
    /// <summary>
    /// 409 (No 161, fail-closed; recorded under the key): the rejection is refused while any order converted from a quote
    /// covering the request is not cancelled. The value keeps its first wire name.
    /// </summary>
    public const string HasOrder = SalesRuleCodes.RequestHasApprovedOrder;
}

/// <summary>The versioned command names of the request attempts (ADR-011 §2), recorded in <c>sales.RequestAttempts</c> (ADR-SAL-001 §2).</summary>
public static class SalesRequestCommands
{
    public const string CreateV1 = "sales.request.create.v1";
    public const string ApproveV1 = "sales.request.approve.v1";
    public const string RejectV1 = "sales.request.reject.v1";

    public static bool IsRequestCommand(string commandName) => commandName is CreateV1 or ApproveV1 or RejectV1;
}

/// <summary>The strict request bodies (shape only; owners, scale and the rules are the service's and the domain's).</summary>
public static class SalesRequestJson
{
    public static CreateSalesRequestRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesRequestRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty
            || request.Lines is null || request.Lines.Count is < 1 or > SalesRequest.MaximumLines
            || request.Note?.Length > SalesRequest.MaximumNoteLength)
            throw new JsonException("Invalid request fields.");
        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId)
                || line.ProductId == Guid.Empty || line.OrderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(line.ExpectedProductVersion) || line.ExpectedProductVersion.Length > 128
                || line.Note?.Length > SalesRequest.MaximumNoteLength)
                throw new JsonException("Invalid line fields.");
            SalesDraftJson.RequireDecimalShape(line.Quantity, positive: true);
        }

        return request;
    }
}

/// <summary>
/// The canonical content of a request command for the replay identity (ADR-011 §3): the create as the strict wire shape
/// serialises it (line order matters, the quantity text is content), and a decision as its command, request, expected
/// version and reason.
/// </summary>
public static class SalesRequestHash
{
    public static string Compute(CreateSalesRequestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = SalesRequestCommands.CreateV1, request }, SalesDraftJson.Options));
    }

    public static string Compute(string commandName, Guid requestId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(new { command = commandName, requestId, request.ExpectedVersion, request.Reason },
            SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>What the store did for a request command: the request after it, recorded now or replayed, and the first correlation.</summary>
public sealed record SalesRequestStoreResult(SalesRequest Request, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>What a request command's key already holds: the stored request or the recorded terminal refusal — never both.</summary>
public sealed record SalesRequestReplay(SalesRequest? Request, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>One page of the company's requests, newest first; a null cursor is the end.</summary>
public sealed record SalesRequestPage(IReadOnlyList<SalesRequest> Items, string? NextCursor);

/// <summary>
/// The persistent request store of the server-resolved company, in the Sales schema beside the drafts. One call is one
/// save: the request (or its decision), the attempt record in the Sales attempt table and the audit event commit together
/// or not at all. The attempt rules are the draft's (ADR-011 §2–§8): (company, command, key hash) is the identity; the same
/// key, content and actor answers the stored result; another content or actor is
/// <see cref="SalesDraftStoreCodes.SameKeyPayloadMismatch"/>; a lost race is <see cref="SalesDraftStoreCodes.ConcurrentChange"/>
/// with nothing stored. Another company's request is null (never 403).
/// </summary>
public interface ISalesRequestStore
{
    Task<SalesRequestStoreResult> StoreRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesRequest> build, CancellationToken cancellationToken = default);

    Task<SalesRequestReplay?> FindRequestReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>ADR-011 §7: records a terminal refusal of a request command under its key (create-time refusals name no request).</summary>
    Task RecordRequestRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? requestId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>
    /// One decision on the company's stored request in one save. <paramref name="mutate"/> is the domain's decision (it names
    /// the expected version and throws the rule refusal; nothing is written then). Null when the request does not exist here.
    /// </summary>
    Task<SalesRequestStoreResult?> DecideRequestAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid requestId, Action<SalesRequest, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    Task<SalesRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<SalesRequestPage> ListRequestsAsync(int pageSize, string? cursor, SalesRequestState? state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// API-20 / API-21: the sales request use cases. Company, actor and authority come from the authenticated server scope and
/// are measured afresh on every call (the draft service's fence); a request of another company is 404.
/// Create, list and detail need <c>sales.requests.write</c> / <c>sales.requests.read</c>; the approval and the rejection
/// need <c>sales.requests.approve</c>.
/// </summary>
public interface ISalesRequestService
{
    Task<SalesRequestResponse> CreateRequestAsync(CreateSalesRequestRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesRequestListResponse> GetRequestListAsync(SalesRequestListQuery query, CancellationToken cancellationToken = default);

    Task<SalesRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default);

    Task<SalesRequestResponse> ApproveRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesRequestResponse> RejectRequestAsync(Guid requestId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}
