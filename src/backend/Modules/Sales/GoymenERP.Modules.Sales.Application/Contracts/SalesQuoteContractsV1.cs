using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// API-22 / API-23 / API-24 (plan §3.2, S-01; delegation No 162, 163; master §7.2): the sales quote wire contract under
// /api/v1/sales/quotes — create / list / detail / revision, the loss with its reason, and the conversion into ONE order
// through the draft's own creation path. The commands use the draft routes' metadata and replay rules (ADR-011).

public sealed record CreateSalesQuoteRequest(
    Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate,
    IReadOnlyList<SalesQuoteLineRequest> Lines,
    IReadOnlyList<Guid>? RequestIds = null,
    DateOnly? ValidUntil = null, DateOnly? RequestedDeliveryDate = null, string? Note = null);

public sealed record SalesQuoteLineRequest(
    Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid OrderUnitId, string Quantity,
    string? UnitPrice = null, Guid? PriceUnitId = null, string? DiscountPercent = null,
    DateOnly? RequestedDeliveryDate = null, string? Note = null);

/// <summary>API-22: the next revision of an open quote — every line and the terms replaced; the customer, currency, date and requests stay.</summary>
public sealed record SalesQuoteRevisionRequest(
    string ExpectedVersion, IReadOnlyList<SalesQuoteLineRequest> Lines,
    DateOnly? ValidUntil = null, DateOnly? RequestedDeliveryDate = null, string? Note = null);

/// <summary>API-24: the loss of an open quote with its free-text reason (required).</summary>
public sealed record SalesQuoteLossRequest(string ExpectedVersion, string Reason);

/// <summary>API-23: "Siparişe dönüştür" — the quote's expected version and the order's business date (the client names it, as on a create).</summary>
public sealed record SalesQuoteConversionRequest(string ExpectedVersion, DateOnly BusinessDate);

public sealed record SalesQuoteSummaryView(
    Guid Id, Guid CompanyId, string DocumentNo, string State, string Version, string Revision,
    Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate, DateOnly? ValidUntil,
    DateTimeOffset CreatedAtUtc, int LineCount, Guid? ConvertedDraftId);

/// <summary>
/// A quoted line. The three commercial texts are disclosed under the order's narrow price read (<c>sales.orders.price.read</c>)
/// exactly as on the order detail: without it they are null and <see cref="PriceDisclosure"/> says Withheld.
/// </summary>
public sealed record SalesQuoteLineView(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion, string ProductCode, string ProductName,
    Guid OrderUnitId, string OrderUnitCode, string Quantity,
    string? UnitPrice, Guid? PriceUnitId, string? DiscountPercent, SalesPriceDisclosure PriceDisclosure,
    DateOnly? RequestedDeliveryDate, string? Note);

public sealed record SalesQuoteLossV1(string Reason, string LostBy, DateTimeOffset LostAtUtc);

public sealed record SalesQuoteConversionV1(Guid DraftId, string ConvertedBy, DateTimeOffset ConvertedAtUtc);

public sealed record SalesQuoteDetailView(
    SalesQuoteSummaryView Quote, IReadOnlyList<Guid> RequestIds, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesQuoteLineView> Lines, SalesQuoteLossV1? Loss, SalesQuoteConversionV1? Conversion);

public sealed record SalesQuoteResponse(SalesQuoteDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record SalesQuoteListResponse(IReadOnlyList<SalesQuoteSummaryView> Items, string? NextCursor);

/// <param name="State">An exact state name (<c>Open</c>, <c>Converted</c>, <c>Lost</c>) to narrow the list to, or null.</param>
public sealed record SalesQuoteListQuery(int PageSize = 50, string? Cursor = null, string? State = null);

/// <summary>The problem codes the quote routes add (contract deltas of S4; the domain's own codes travel as they are).</summary>
public static class SalesQuoteProblemCodesV1
{
    /// <summary>422: a named request is not this company's request (unknown here).</summary>
    public const string RequestUnknown = "sales_quote_request_unknown";

    /// <summary>422 (No 163): a named request is another customer's — a quote covers requests of ONE customer.</summary>
    public const string RequestCustomerMismatch = "sales_quote_request_customer_mismatch";

    /// <summary>422: a named request is rejected, or already covered by another open or converted quote (talep → teklif N:1).</summary>
    public const string RequestUnavailable = "sales_quote_request_unavailable";

    /// <summary>
    /// 409 (No 160, default-deny; field <c>requestIds</c>): a request the quote covers is not approved — at the conversion, and
    /// at the converted order's submission or approval. Not recorded: the request may still be approved and the key retried.
    /// </summary>
    public const string RequestNotApproved = SalesRuleCodes.QuoteRequestNotApproved;

    /// <summary>
    /// 409 (SAT-D24 interim, fail-closed; field <c>validUntil</c>): the quote's validity date precedes the conversion's
    /// business date. SAT-D24 converts an expired quote only with a manager approval, which is a follow-up step; until it exists
    /// the conversion is refused. Not recorded (the No 160 pattern): the approval is meant to clear it for the same request.
    /// </summary>
    public const string Expired = SalesRuleCodes.QuoteExpired;

    /// <summary>
    /// 403 (service code; the wire's 403 stays code-less like every platform 403): a quote revision, or a quote create that
    /// carries a price field, by an actor whose prices are WITHHELD (no <c>sales.orders.price.read</c>). A revision replaces
    /// every line, so a caller who cannot see the prices would erase them; fail-closed. Writing a quote price needs no more than
    /// <c>sales.quotes.write</c> (SAT-D26).
    /// </summary>
    public const string PriceReadRequired = "price_read_required";
}

public static class SalesQuoteApiV1
{
    public const string QuotePath = "/api/v1/sales/quotes";
    public const int CreatedStatus = 201;
    public const int CommandStatus = 200;
}

public static class SalesQuoteCommands
{
    public const string CreateV1 = "sales.quote.create.v1";
    public const string ReviseV1 = "sales.quote.revise.v1";
    public const string LoseV1 = "sales.quote.lose.v1";

    /// <summary>No 162: the conversion's attempt is recorded under its own name and answers the ORDER it created (a draft attempt).</summary>
    public const string ConvertV1 = "sales.quote.convert.v1";

    public static bool IsQuoteCommand(string commandName) => commandName is CreateV1 or ReviseV1 or LoseV1;
}

public static class SalesQuoteJson
{
    public static CreateSalesQuoteRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesQuoteRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (request.PartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.CurrencyId == Guid.Empty
            || request.Note?.Length > SalesQuote.MaximumNoteLength
            || request.RequestIds is { } ids && (ids.Count > SalesQuote.MaximumRequests || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count))
            throw new JsonException("Invalid quote fields.");
        RequireLines(request.Lines);
        return request;
    }

    public static SalesQuoteRevisionRequest ReadRevision(string json)
    {
        var request = JsonSerializer.Deserialize<SalesQuoteRevisionRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        if (request.Note?.Length > SalesQuote.MaximumNoteLength)
            throw new JsonException("Invalid note.");
        RequireLines(request.Lines);
        return request;
    }

    public static SalesQuoteLossRequest ReadLoss(string json)
    {
        var request = JsonSerializer.Deserialize<SalesQuoteLossRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        if (request.Reason is null || request.Reason.Length > SalesQuote.MaximumReasonLength)
            throw new JsonException("Invalid reason.");
        return request;
    }

    public static SalesQuoteConversionRequest ReadConversion(string json)
    {
        var request = JsonSerializer.Deserialize<SalesQuoteConversionRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        return request;
    }

    private static void RequireVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 19 || !Regex.IsMatch(version, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
    }

    private static void RequireLines(IReadOnlyList<SalesQuoteLineRequest>? lines)
    {
        if (lines is null || lines.Count is < 1 or > SalesQuote.MaximumLines)
            throw new JsonException("Invalid lines.");
        var ids = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.ProductId == Guid.Empty
                || line.OrderUnitId == Guid.Empty || string.IsNullOrWhiteSpace(line.ExpectedProductVersion)
                || line.ExpectedProductVersion.Length > 128 || line.Note?.Length > SalesQuote.MaximumNoteLength
                || line.PriceUnitId == Guid.Empty || (line.UnitPrice is null) != (line.PriceUnitId is null))
                throw new JsonException("Invalid line fields.");
            SalesDraftJson.RequireDecimalShape(line.Quantity, positive: true);
            if (line.UnitPrice is not null) SalesDraftJson.RequireDecimalShape(line.UnitPrice, positive: false);
            if (line.DiscountPercent is not null) SalesDraftJson.RequireDecimalShape(line.DiscountPercent, positive: false);
        }
    }
}

public static class SalesQuoteHash
{
    public static string Compute(CreateSalesQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = SalesQuoteCommands.CreateV1, request }, SalesDraftJson.Options));
    }

    public static string Compute(Guid quoteId, SalesQuoteRevisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = SalesQuoteCommands.ReviseV1, quoteId, request }, SalesDraftJson.Options));
    }

    public static string Compute(Guid quoteId, SalesQuoteLossRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = SalesQuoteCommands.LoseV1, quoteId, request }, SalesDraftJson.Options));
    }

    public static string Compute(Guid quoteId, SalesQuoteConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = SalesQuoteCommands.ConvertV1, quoteId, request }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

public sealed record SalesQuoteStoreResult(SalesQuote Quote, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

public sealed record SalesQuoteReplay(SalesQuote? Quote, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

public sealed record SalesQuotePage(IReadOnlyList<SalesQuote> Items, string? NextCursor);

/// <summary>
/// The persistent quote store of the server-resolved company (the Sales schema, the draft's attempt table and rules).
/// <see cref="StoreConvertedDraftAsync"/> is the conversion's ONE save: the new order (header, lines), its source-quote
/// reference (unique per quote), the quote's Converted state, the attempt and both audit events — or nothing.
/// </summary>
public interface ISalesQuoteStore
{
    Task<SalesQuoteStoreResult> StoreQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, Func<string, SalesQuote> build,
        CancellationToken cancellationToken = default);

    Task<SalesQuoteReplay?> FindQuoteReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    Task RecordQuoteRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? quoteId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>A revision or the loss of the company's stored quote in one save; null when the quote does not exist here.</summary>
    Task<SalesQuoteStoreResult?> UpdateQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid quoteId,
        Action<SalesQuote, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    Task<SalesQuote?> FindQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default);

    Task<SalesQuotePage> ListQuotesAsync(int pageSize, string? cursor, SalesQuoteState? state, CancellationToken cancellationToken = default);

    /// <summary>Whether the request is covered by an open or converted quote of this company (talep → teklif N:1).</summary>
    Task<bool> IsRequestQuotedAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// No 162: the conversion's ONE save. <paramref name="build"/> receives the order's document number and returns the order
    /// built by the draft's own rules; the store re-reads the quote (tracked), checks it with <see cref="SalesQuote.Convert"/>
    /// (expected version, still open, not expired on the order's business date — SAT-D24 interim), checks that every request it covers is approved (No 160), writes the order with its
    /// source-quote reference (UX_Drafts_SourceQuoteId: one order per quote), the quote's conversion, the attempt (answering
    /// the order) and both audit events. A same-key replay answers the stored order.
    /// </summary>
    Task<SalesDraftStoreResult> StoreConvertedDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid quoteId,
        long expectedQuoteVersion, Func<string, SalesOrderDraft> build, CancellationToken cancellationToken = default);
}

/// <summary>
/// API-22 / API-23 / API-24: the sales quote use cases. Create, revision and loss need <c>sales.quotes.write</c>; list and
/// detail <c>sales.quotes.read</c>; the conversion needs <c>sales.quotes.write</c> AND <c>sales.drafts.create</c> (and, for a
/// priced line, the order path's own <c>sales.orders.price.write</c>).
/// </summary>
public interface ISalesQuoteService
{
    Task<SalesQuoteResponse> CreateQuoteAsync(CreateSalesQuoteRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesQuoteListResponse> GetQuoteListAsync(SalesQuoteListQuery query, CancellationToken cancellationToken = default);

    Task<SalesQuoteDetailView> GetQuoteDetailAsync(Guid quoteId, CancellationToken cancellationToken = default);

    Task<SalesQuoteResponse> ReviseQuoteAsync(Guid quoteId, SalesQuoteRevisionRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesQuoteResponse> RecordQuoteLossAsync(Guid quoteId, SalesQuoteLossRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesOrderDraftResponse> ConvertQuoteAsync(Guid quoteId, SalesQuoteConversionRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}
