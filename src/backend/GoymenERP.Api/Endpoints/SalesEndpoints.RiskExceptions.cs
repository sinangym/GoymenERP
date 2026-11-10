using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-07 (EK21 SAT-D153…D162; Sinan 2026-11-10, Issue #66 / EK55 pending): the single-shipment risk exception routes of an
/// order over <see cref="ISalesRiskExceptionService"/>. The route binds only the server context and the strict body; the service
/// decides (authority first, replay, the order, one save). The draft routes' wire rules apply unchanged: exactly one canonical
/// <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400
/// <c>validation_failed</c>), the replay headers on the same key again (ADR-011 §4), another company's order 404, and every
/// 401/403/404/503 code-less. There is no date-range (customer) variant (EK21). No answer carries an amount (SAT-D162).
/// </summary>
public static partial class SalesEndpoints
{
    private static void MapRiskExceptions(RouteGroupBuilder drafts)
    {
        // The order's exceptions under `sales.drafts.read`, newest first (technical bound SalesRiskExceptionApiV1.MaximumListed).
        drafts.MapGet("/{id:guid}/risk-exceptions", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesRiskExceptionService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetRiskExceptionsAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderRiskExceptionsV1")
            .Produces<SalesRiskExceptionListResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D159: the request under `sales.orders.risk-exception.request` only (never `sales.drafts.read`). The strict body
        // `{loadingInstructionId, reason}`; 201 with the exception. A pending or today-valid exception of the same shipment and an
        // order that is not approved/open are 409s that are NOT recorded under the key (they can change).
        drafts.MapPost("/{id:guid}/risk-exceptions", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesRiskExceptionService service,
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

            RequestSalesRiskExceptionRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesRiskExceptionJson.ReadRequest(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.RequestRiskExceptionAsync(id, request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.Exception, SalesDraftJson.Options, statusCode: SalesRiskExceptionApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("RequestSalesOrderRiskExceptionV1")
            .WithIdempotencyContract(SalesRiskExceptionApiV1.CreatedStatus)
            .Accepts<RequestSalesRiskExceptionRequest>("application/json")
            .Produces<SalesRiskExceptionView>(SalesRiskExceptionApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesRiskExceptionProblemCodesV1.Duplicate, SalesRiskExceptionProblemCodesV1.OrderNotEligible,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", SalesRuleCodes.RiskExceptionReasonTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D153: the decision under `sales.orders.risk-exception.approve`. The strict body `{expectedVersion}` (+ `reason` on the
        // rejection); 200 with the exception. A stale version, a wrong state or a self-decision is the key's recorded 409 (§7).
        MapRiskExceptionDecision(drafts, "approval", "ApproveSalesOrderRiskExceptionV1", carriesReason: false, SalesRiskExceptionJson.ReadApproval,
            (service, id, no, body, metadata, cancellationToken) =>
                service.ApproveRiskExceptionAsync(id, no, body, metadata, cancellationToken));
        MapRiskExceptionDecision(drafts, "rejection", "RejectSalesOrderRiskExceptionV1", carriesReason: true, SalesRiskExceptionJson.ReadRejection,
            (service, id, no, body, metadata, cancellationToken) =>
                service.RejectRiskExceptionAsync(id, no, body, metadata, cancellationToken));
    }

    private static void MapRiskExceptionDecision(RouteGroupBuilder drafts, string segment, string name, bool carriesReason,
        Func<string, SalesDraftStateCommandRequest> read,
        Func<ISalesRiskExceptionService, Guid, int, SalesDraftStateCommandRequest, SalesCommandMetadata, CancellationToken,
            Task<SalesRiskExceptionCommandResult>> command)
    {
        var route = drafts.MapPost($"/{{id:guid}}/risk-exceptions/{{no:int:min(1)}}/{segment}", async (
            Guid id,
            int no,
            HttpContext httpContext,
            [FromServices] ISalesRiskExceptionService service,
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
                body = read(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await command(service, id, no, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.Exception, SalesDraftJson.Options, statusCode: SalesRiskExceptionApiV1.CommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName(name)
            .WithIdempotencyContract(SalesRiskExceptionApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesRiskExceptionView>(SalesRiskExceptionApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesRiskExceptionProblemCodesV1.SelfDecision, SalesRiskExceptionProblemCodesV1.OrderNotEligible,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange);

        // Only the rejection carries a reason, so only it can answer 422 (a blank or over-long rejection reason).
        if (carriesReason)
        {
            route.ProducesErpUnprocessableEntity("validation_failed", SalesRuleCodes.RiskExceptionReasonTooLong);
        }

        route.ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
