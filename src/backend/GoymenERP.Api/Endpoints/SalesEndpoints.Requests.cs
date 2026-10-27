using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-20 / API-21 (plan §3.2, S-01 "Satış Talebi / Teklif Havuzu"; delegation No 160, 161): the sales request routes over
/// <see cref="ISalesRequestService"/>. The route binds only the server context and the strict body; the service decides
/// (authority first, replay, owners, domain, one save). The draft routes' wire rules apply unchanged: exactly one canonical
/// <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400
/// <c>validation_failed</c>), the replay headers on the same key again (ADR-011 §4/§7), another company's request 404, and
/// every 401/403/404/503 code-less (the platform's public answer).
/// </summary>
public static partial class SalesEndpoints
{
    public const string RequestsPath = SalesRequestApiV1.RequestPath;
    private static readonly HashSet<string> RequestListQueryNames = new(["pageSize", "cursor", "state"], StringComparer.Ordinal);
    private const int MaximumRequestStateLength = 32;

    private static void MapRequestEndpoints(IEndpointRouteBuilder endpoints)
    {
        var requests = endpoints.MapGroup(RequestsPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        requests.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
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

            CreateSalesRequestRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesRequestJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.CreateRequestAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location = RequestLocation(httpContext.Request, response.Detail.Request.Id);
                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesRequestApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("CreateSalesRequestV1")
            // The replayable results: the stored request (201) and the recorded terminal refusal (409, §7).
            .WithIdempotencyContract(SalesRequestApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreateSalesRequestRequest>("application/json")
            .Produces<SalesRequestResponse>(SalesRequestApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.VersionConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", "unsupported_product", "unsupported_unit")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        requests.MapGet("/", async (
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromQuery] string? state,
            HttpContext httpContext,
            [FromServices] ISalesRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, RequestListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength
                || state is { Length: 0 or > MaximumRequestStateLength })
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetRequestListAsync(new SalesRequestListQuery(pageSize ?? 50, cursor, state), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesRequestsV1")
            .Produces<SalesRequestListResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        requests.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetRequestDetailAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesRequestV1")
            .Produces<SalesRequestDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // API-21: the decision commands. The strict body `{expectedVersion}` (+ `reason` on the rejection only — required there
        // by the domain, 422 when blank); 200 with the request after the decision, and 200 + replay headers for the same key
        // again (ADR-011 §4); a stale version or a wrong state is the key's recorded 409 (§7).
        MapRequestDecision(requests, "approval", "ApproveSalesRequestV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.ApproveRequestAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: [], extraConflictCodes: []);
        // No 161 (fail-closed): a request with any order that is not cancelled is refused (409 sales_request_has_approved_order).
        MapRequestDecision(requests, "rejection", "RejectSalesRequestV1", allowReason: true,
            (service, id, body, metadata, cancellationToken) => service.RejectRequestAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: ["sales_request_rejection_reason_required", "sales_request_rejection_reason_too_long"],
            extraConflictCodes: [SalesRequestProblemCodesV1.HasOrder]);
    }

    private static void MapRequestDecision(RouteGroupBuilder requests, string segment, string name, bool allowReason,
        Func<ISalesRequestService, Guid, SalesDraftStateCommandRequest, SalesCommandMetadata, CancellationToken, Task<SalesRequestResponse>> command,
        string[] unprocessableCodes, string[] extraConflictCodes)
    {
        var route = requests.MapPost($"/{{id:guid}}/{segment}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
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

            SalesDraftStateCommandRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadStateCommand(await reader.ReadToEndAsync(cancellationToken), allowReason);
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await command(service, id, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                }

                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesRequestApiV1.CommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName(name)
            // The replayable results: the request after the decision (200) and the recorded terminal refusals (409, §7).
            .WithIdempotencyContract(SalesRequestApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesRequestResponse>(SalesRequestApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange, .. extraConflictCodes])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        if (unprocessableCodes.Length > 0)
        {
            route.ProducesErpUnprocessableEntity(unprocessableCodes);
        }
    }

    /// <summary>A refusal of a request command: a replayed refusal carries its first correlation in the replay headers.</summary>
    private static IResult RequestProblem(HttpContext httpContext, SalesDraftServiceException exception, ICorrelationContext correlation)
    {
        if (exception.IsReplay)
        {
            ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
        }

        return DraftProblem(exception, correlation);
    }

    /// <summary>The created request's absolute location on this host (the detail route).</summary>
    private static string RequestLocation(HttpRequest request, Guid requestId) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{RequestsPath}/{requestId:D}";
}
