using System.Text;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-04 (Satış hazırlık planı v1 §3.1; DEC-SAL-002, DEC-SAL-008): the order header revision and the read of the revisable
/// header. The source-owned service enforces <c>sales.orders.header.write</c> (the read: <c>sales.drafts.read</c>, the due
/// terms only with <c>sales.orders.price.read</c>), version/state/idempotency, the reason, the owners of what changes and the
/// fail-closed customer/currency rules. The route owns only the strict HTTP envelope (the quantity revision's) and never derives
/// a customer, currency, due date, role or re-approval.
/// </summary>
public static partial class SalesEndpoints
{
    private static void MapHeaderRevisionEndpoints(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/header", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftHeaderAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftHeaderV1")
            .Produces<SalesDraftHeaderV1>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapPost("/{id:guid}/header-revision", async (
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

            SalesDraftHeaderRevisionRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftHeaderApiV1.Read(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.ReviseHeaderAsync(id, body,
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
            .WithName("ReviseSalesOrderDraftHeaderV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftHeaderRevisionRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.RevisionBlocked,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", SalesDraftProblemCodesV1.OwnerReferenceInvalid,
                SalesDraftProblemCodesV1.UnsupportedCurrency)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
