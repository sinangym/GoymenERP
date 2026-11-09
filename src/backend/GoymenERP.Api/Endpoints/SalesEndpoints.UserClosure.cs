using System.Text;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D32 / SAT-D168 / SAT-D215: the user closure of a "kapanmaya hazır" order and the read of the recorded user closure. The
/// source-owned service enforces <c>sales.orders.close</c> (the read: <c>sales.drafts.read</c>), version/state/idempotency and
/// the readiness recomputed inside the save from the status read's own calculation. The route owns only the strict HTTP
/// envelope (the force-close's, without a reason or a token) and never derives readiness, a role or a closure.
/// </summary>
public static partial class SalesEndpoints
{
    private static void MapUserClosureEndpoints(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/close", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
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

            SalesDraftCloseRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadClose(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.CloseDraftAsync(id, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                }

                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.StateCommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                if (exception.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                        ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
                }

                return DraftProblem(exception, correlation);
            }
        })
            .WithName("CloseSalesOrderDraftV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftCloseRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.OrderNotReadyToClose, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapGet("/{id:guid}/close", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftUserClosureAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftUserClosureV1")
            .Produces<SalesOrderDraftUserClosureResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
