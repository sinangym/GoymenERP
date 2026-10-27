using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-22 / API-23 / API-24 (plan §3.2, S-01; delegation No 160, 162, 163; master §7.2): the sales quote routes over
/// <see cref="ISalesQuoteService"/> — create (201), list, detail, revision (200), loss (200; v2, SAT-D25), the expired-quote
/// approval (200; SAT-D24/D44) and the conversion into its ONE order (201 + the order's <c>Location</c>, the order's detail as
/// the body), plus the two reads of the create / loss forms: the SAT-D25 loss reason list and the SAT-D39 validity suggestion.
/// The draft routes' wire rules apply unchanged.
/// </summary>
public static partial class SalesEndpoints
{
    public const string QuotesPath = SalesQuoteApiV1.QuotePath;
    private static readonly HashSet<string> QuoteListQueryNames = new(["pageSize", "cursor", "state"], StringComparer.Ordinal);
    private static readonly HashSet<string> QuoteValiditySuggestionQueryNames = new(["businessDate"], StringComparer.Ordinal);

    private static void MapQuoteEndpoints(IEndpointRouteBuilder endpoints)
    {
        var quotes = endpoints.MapGroup(QuotesPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        quotes.MapPost("/", (HttpContext httpContext, [FromServices] ISalesQuoteService service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            QuoteCommandAsync(httpContext, correlation, SalesQuoteJson.ReadCreate, SalesQuoteApiV1.CreatedStatus,
                (body, metadata) => service.CreateQuoteAsync(body, metadata, cancellationToken),
                response => (response.IsReplay, response.OriginalCorrelationId, QuoteLocation(httpContext.Request, response.Detail.Quote.Id)),
                cancellationToken))
            .WithName("CreateSalesQuoteV1")
            .WithIdempotencyContract(SalesQuoteApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreateSalesQuoteRequest>("application/json")
            .Produces<SalesQuoteResponse>(SalesQuoteApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.VersionConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", "unsupported_product", "unsupported_unit",
                SalesDraftProblemCodesV1.UnsupportedCurrency, SalesQuoteProblemCodesV1.RequestUnknown,
                SalesQuoteProblemCodesV1.RequestCustomerMismatch, SalesQuoteProblemCodesV1.RequestUnavailable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        quotes.MapGet("/", async (
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromQuery] string? state,
            HttpContext httpContext,
            [FromServices] ISalesQuoteService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, QuoteListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength
                || state is { Length: 0 or > MaximumRequestStateLength })
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetQuoteListAsync(new SalesQuoteListQuery(pageSize ?? 50, cursor, state), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesQuotesV1")
            .Produces<SalesQuoteListResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D25: the company's closed loss reason list in force on the server's business day (isDefined false = no list:
        // a loss is refused). Under the quote write name, like the loss itself.
        quotes.MapGet("/loss-reasons", async (
            HttpContext httpContext,
            [FromServices] ISalesQuoteService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count > 0)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetQuoteLossReasonsAsync(cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesQuoteLossReasonsV1")
            .Produces<SalesQuoteLossReasonsViewV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D39: the default validity suggested for a new quote on its business date (nulls = no Settings value, no suggestion).
        quotes.MapGet("/validity-suggestion", async (
            [FromQuery] DateOnly businessDate,
            HttpContext httpContext,
            [FromServices] ISalesQuoteService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, QuoteValiditySuggestionQueryNames, QuoteValiditySuggestionQueryNames)
                || !HasExactDate(httpContext.Request.Query, "businessDate"))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetQuoteValiditySuggestionAsync(businessDate, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesQuoteValiditySuggestionV1")
            .Produces<SalesQuoteValiditySuggestionV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        quotes.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesQuoteService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetQuoteDetailAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesQuoteV1")
            .Produces<SalesQuoteDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        quotes.MapPost("/{id:guid}/revision", (Guid id, HttpContext httpContext, [FromServices] ISalesQuoteService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            QuoteCommandAsync(httpContext, correlation, SalesQuoteJson.ReadRevision, SalesQuoteApiV1.CommandStatus,
                (body, metadata) => service.ReviseQuoteAsync(id, body, metadata, cancellationToken),
                response => (response.IsReplay, response.OriginalCorrelationId, null),
                cancellationToken))
            .WithName("ReviseSalesQuoteV1")
            .WithIdempotencyContract(SalesQuoteApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesQuoteRevisionRequest>("application/json")
            .Produces<SalesQuoteResponse>(SalesQuoteApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", "unsupported_product", "unsupported_unit")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // API-24 v2 (SAT-D25): the loss names an entry of the Settings list; it retires the free-text v1 on the same path.
        quotes.MapPost("/{id:guid}/loss", (Guid id, HttpContext httpContext, [FromServices] ISalesQuoteService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            QuoteCommandAsync(httpContext, correlation, SalesQuoteJson.ReadLossV2, SalesQuoteApiV1.CommandStatus,
                (body, metadata) => service.RecordQuoteLossV2Async(id, body, metadata, cancellationToken),
                response => (response.IsReplay, response.OriginalCorrelationId, null),
                cancellationToken))
            .WithName("RecordSalesQuoteLossV2")
            .WithIdempotencyContract(SalesQuoteApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesQuoteLossRequestV2>("application/json")
            .Produces<SalesQuoteResponse>(SalesQuoteApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(SalesQuoteProblemCodesV1.LossReasonUnknown, SalesQuoteProblemCodesV1.LossExplanationRequired,
                SalesQuoteProblemCodesV1.LossExplanationTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D24 / SAT-D44: the manager approval that lets the current revision of an EXPIRED quote be converted.
        quotes.MapPost("/{id:guid}/expiry-approval", (Guid id, HttpContext httpContext, [FromServices] ISalesQuoteService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            QuoteCommandAsync(httpContext, correlation, SalesQuoteJson.ReadExpiryApproval, SalesQuoteApiV1.CommandStatus,
                (body, metadata) => service.ApproveExpiredQuoteConversionAsync(id, body, metadata, cancellationToken),
                response => (response.IsReplay, response.OriginalCorrelationId, null),
                cancellationToken))
            .WithName("ApproveExpiredSalesQuoteConversionV1")
            .WithIdempotencyContract(SalesQuoteApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesQuoteExpiryApprovalRequest>("application/json")
            .Produces<SalesQuoteResponse>(SalesQuoteApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesQuoteProblemCodesV1.NotExpired, SalesQuoteProblemCodesV1.ExpiryAlreadyApproved,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // API-23 (No 162): the conversion answers the ORDER it created (201 + the order's Location), and the same key again
        // the same order with the replay headers.
        quotes.MapPost("/{id:guid}/conversion", (Guid id, HttpContext httpContext, [FromServices] ISalesQuoteService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            QuoteCommandAsync(httpContext, correlation, SalesQuoteJson.ReadConversion, SalesDraftApiV1.CreatedStatus,
                (body, metadata) => service.ConvertQuoteAsync(id, body, metadata, cancellationToken),
                response => (response.IsReplay, response.OriginalCorrelationId, DraftLocation(httpContext.Request, response.Detail.Draft.Id)),
                cancellationToken))
            .WithName("ConvertSalesQuoteV1")
            .WithIdempotencyContract(SalesDraftApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesQuoteConversionRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.VersionConflict,
                SalesDraftProblemCodesV1.StateConflict, SalesQuoteProblemCodesV1.RequestNotApproved, SalesQuoteProblemCodesV1.Expired,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", "unsupported_product", "unsupported_unit",
                SalesDraftProblemCodesV1.UnsupportedCurrency)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The shared shape of a quote command: one canonical Idempotency-Key (400), JSON only (415), the strict body (400), the
    /// service's answer with the replay headers when it was answered before, and the refusals as the draft routes answer them.
    /// </summary>
    private static async Task<IResult> QuoteCommandAsync<TBody, TResponse>(HttpContext httpContext, ICorrelationContext correlation,
        Func<string, TBody> read, int status, Func<TBody, SalesCommandMetadata, Task<TResponse>> command,
        Func<TResponse, (bool IsReplay, string OriginalCorrelationId, string? Location)> describe, CancellationToken cancellationToken)
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return DraftProblem(StatusCodes.Status400BadRequest, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TBody body;
        try
        {
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            body = read(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (JsonException)
        {
            return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
        }

        try
        {
            var response = await command(body,
                new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            var (isReplay, originalCorrelationId, location) = describe(response);
            if (isReplay)
            {
                ApplyReplayHeaders(httpContext, originalCorrelationId);
            }

            if (location is not null)
            {
                httpContext.Response.Headers.Location = location;
            }

            return Results.Json(response, SalesDraftJson.Options, statusCode: status);
        }
        catch (SalesDraftServiceException exception)
        {
            return RequestProblem(httpContext, exception, correlation);
        }
    }

    private static string QuoteLocation(HttpRequest request, Guid quoteId) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{QuotesPath}/{quoteId:D}";
}
