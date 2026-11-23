using System.Text;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP4 (SAT-D475 / SAT-D476): the approval request routes over <see cref="ISalesApprovalRequestService"/>. The route binds only
/// the server context and the strict body; the service decides (authority first, replay, the subject, one save). The draft
/// routes' wire rules apply unchanged: exactly one canonical <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>),
/// JSON only (415), the strict body (400 <c>validation_failed</c>), the replay headers on the same key again (ADR-011 §4),
/// another company's subject 404, and every 401/403/404/503 code-less. The over-delivery request is opened by the Logistics
/// shipment gate through the Sales port, never by a route.
/// </summary>
public static partial class SalesEndpoints
{
    private const int ApprovalRequestCreatedStatus = StatusCodes.Status201Created;
    private const int ApprovalRequestCommandStatus = StatusCodes.Status200OK;

    private static readonly string[] ApprovalRequestDecisionConflicts =
    [
        SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict, SalesApprovalRequestProblemCodesV1.SelfDecision,
        SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange,
    ];

    /// <summary>SAT-D476: the quote's conversion requests (read under the quote read; open under the quote write; reject).</summary>
    private static void MapQuoteApprovalRequests(RouteGroupBuilder quotes)
    {
        quotes.MapGet("/{id:guid}/expired-conversion-requests", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetQuoteConversionRequestsAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesQuoteConversionRequestsV1")
            .Produces<SalesApprovalRequestListResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // The strict body `{expectedVersion, note?}`; 201 with the request. A stale quote version, a closed quote, a quote not
        // expired on the server's day or already approved is the key's recorded 409; a pending request of the same revision is a
        // 409 that is NOT recorded (it can be rejected).
        quotes.MapPost("/{id:guid}/expired-conversion-requests", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
            await ApprovalRequestCommandAsync(httpContext, correlation, SalesApprovalRequestJson.ReadQuoteRequest,
                ApprovalRequestCreatedStatus,
                (body, metadata) => service.RequestQuoteConversionAsync(id, body, metadata, cancellationToken), cancellationToken))
            .WithName("RequestSalesQuoteConversionV1")
            .WithIdempotencyContract(ApprovalRequestCreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesQuoteConversionRequestRequest>("application/json")
            .Produces<SalesApprovalRequestView>(ApprovalRequestCreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesApprovalRequestProblemCodesV1.QuoteNotExpired, SalesApprovalRequestProblemCodesV1.QuoteAlreadyApproved,
                SalesApprovalRequestProblemCodesV1.Duplicate, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(SalesApprovalRequestProblemCodesV1.TextTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        quotes.MapPost("/{id:guid}/expired-conversion-requests/{no:int:min(1)}/rejection", async (
            Guid id,
            int no,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
            await ApprovalRequestCommandAsync(httpContext, correlation, SalesApprovalRequestJson.ReadRejection, ApprovalRequestCommandStatus,
                (body, metadata) => service.RejectQuoteConversionRequestAsync(id, no, body, metadata, cancellationToken), cancellationToken))
            .WithName("RejectSalesQuoteConversionRequestV1")
            .WithIdempotencyContract(ApprovalRequestCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesApprovalRequestView>(ApprovalRequestCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(ApprovalRequestDecisionConflicts)
            .ProducesErpUnprocessableEntity("validation_failed", SalesApprovalRequestProblemCodesV1.ReasonRequired,
                SalesApprovalRequestProblemCodesV1.TextTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>SAT-D475: the order's over-delivery requests (read under the draft read; approve/reject under the over-delivery approval).</summary>
    private static void MapOverDeliveryRequests(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/over-delivery-requests", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetOverDeliveryRequestsAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderOverDeliveryRequestsV1")
            .Produces<SalesApprovalRequestListResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // The approval re-judges the requested shipped total (Settings tolerance, the one calculator): a block-band total is 422,
        // a request that no longer stands (revision, terminal order, no longer in the approval band) the key's recorded 409.
        drafts.MapPost("/{id:guid}/over-delivery-requests/{no:int:min(1)}/approval", async (
            Guid id,
            int no,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
            await ApprovalRequestCommandAsync(httpContext, correlation, SalesApprovalRequestJson.ReadApproval, ApprovalRequestCommandStatus,
                (body, metadata) => service.ApproveOverDeliveryRequestAsync(id, no, body, metadata, cancellationToken), cancellationToken))
            .WithName("ApproveSalesOrderOverDeliveryRequestV1")
            .WithIdempotencyContract(ApprovalRequestCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesApprovalRequestView>(ApprovalRequestCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. ApprovalRequestDecisionConflicts, SalesApprovalRequestProblemCodesV1.Stale])
            .ProducesErpUnprocessableEntity("validation_failed", SalesApprovalRequestProblemCodesV1.Blocked)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapPost("/{id:guid}/over-delivery-requests/{no:int:min(1)}/rejection", async (
            Guid id,
            int no,
            HttpContext httpContext,
            [FromServices] ISalesApprovalRequestService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
            await ApprovalRequestCommandAsync(httpContext, correlation, SalesApprovalRequestJson.ReadRejection, ApprovalRequestCommandStatus,
                (body, metadata) => service.RejectOverDeliveryRequestAsync(id, no, body, metadata, cancellationToken), cancellationToken))
            .WithName("RejectSalesOrderOverDeliveryRequestV1")
            .WithIdempotencyContract(ApprovalRequestCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesApprovalRequestView>(ApprovalRequestCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(ApprovalRequestDecisionConflicts)
            .ProducesErpUnprocessableEntity("validation_failed", SalesApprovalRequestProblemCodesV1.ReasonRequired,
                SalesApprovalRequestProblemCodesV1.TextTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ApprovalRequestCommandAsync<TBody>(HttpContext httpContext, ICorrelationContext correlation,
        Func<string, TBody> read, int status, Func<TBody, SalesCommandMetadata, Task<SalesApprovalRequestCommandResult>> command,
        CancellationToken cancellationToken)
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
            var result = await command(body,
                new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            if (result.IsReplay)
            {
                ApplyReplayHeaders(httpContext, result.CorrelationId);
            }

            return Results.Json(result.Request, SalesDraftJson.Options, statusCode: status);
        }
        catch (SalesDraftServiceException exception)
        {
            return RequestProblem(httpContext, exception, correlation);
        }
    }
}
