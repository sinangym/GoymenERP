using System.Text;
using System.Text.Json;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP13 (YOL-D11, DALGA_PLANI_v2 1.14): the order's signed approval routes over <see cref="ISalesOrderSignedApprovalServiceV1"/>.
/// The customer's wet-signed approval is first uploaded to the order in the Document Vault (<c>POST /api/v1/documents</c>, owner kind
/// <c>sales.order</c>: the vault's size/type/signature/scan rules, versioned, never deleted); these routes record which vault document
/// is the order's signed approval and withdraw a wrong record with a reason.
/// <list type="bullet">
/// <item><c>GET …/{id}/signed-approvals</c> — the records, newest first, and whether the current revision is covered
/// (<c>sales.drafts.read</c>).</item>
/// <item><c>POST …/{id}/signed-approvals</c> — <c>{documentId, expectedOrderVersion}</c> under
/// <c>sales.orders.signed-approval.record</c>; 201 with the status; the same key answers the same 201 with the replay headers.</item>
/// <item><c>POST …/{id}/signed-approvals/{approvalId}/deactivation</c> — <c>{expectedVersion, reason}</c>; 200.</item>
/// </list>
/// The draft routes' wire rules apply: exactly one canonical <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>), JSON
/// only (415), the strict body (400 <c>validation_failed</c>), another company's order 404, every 401/403/404/503 code-less.
/// </summary>
public static partial class SalesEndpoints
{
    private const int SignedApprovalCreatedStatus = StatusCodes.Status201Created;

    private static void MapOrderSignedApprovals(RouteGroupBuilder drafts)
    {
        drafts.MapGet($"/{{id:guid}}/{SalesOrderSignedApprovalApiV1.Segment}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesOrderSignedApprovalServiceV1 service,
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
                return Results.Json(await service.GetAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("GetSalesOrderSignedApprovalsV1")
            .Produces<SalesOrderSignedApprovalsResponseV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapPost($"/{{id:guid}}/{SalesOrderSignedApprovalApiV1.Segment}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesOrderSignedApprovalServiceV1 service,
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

            RecordSalesOrderSignedApprovalRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesOrderSignedApprovalJson.ReadRecord(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.RecordAsync(id, request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.Detail, SalesDraftJson.Options, statusCode: SignedApprovalCreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("RecordSalesOrderSignedApprovalV1")
            .WithIdempotencyContract(SignedApprovalCreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<RecordSalesOrderSignedApprovalRequest>("application/json")
            .Produces<SalesOrderSignedApprovalsResponseV1>(SignedApprovalCreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved,
                SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapPost($"/{{id:guid}}/{SalesOrderSignedApprovalApiV1.Segment}/{{approvalId:guid}}/{SalesOrderSignedApprovalApiV1.DeactivationSegment}", async (
            Guid id,
            Guid approvalId,
            HttpContext httpContext,
            [FromServices] ISalesOrderSignedApprovalServiceV1 service,
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

            DeactivateSalesOrderSignedApprovalRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesOrderSignedApprovalJson.ReadDeactivation(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.DeactivateAsync(id, approvalId, request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.Detail, SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return RequestProblem(httpContext, exception, correlation);
            }
        })
            .WithName("DeactivateSalesOrderSignedApprovalV1")
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict)
            .Accepts<DeactivateSalesOrderSignedApprovalRequest>("application/json")
            .Produces<SalesOrderSignedApprovalsResponseV1>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(SalesRuleCodes.SignedApprovalReasonRequired, SalesRuleCodes.SignedApprovalReasonTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
