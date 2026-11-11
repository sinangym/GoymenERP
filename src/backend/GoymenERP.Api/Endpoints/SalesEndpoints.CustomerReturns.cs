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
/// API-40 (EK57 SAT-D288…D291, EK58 SAT-D292…D294; Sinan 2026-11-10): the customer return invoice routes over
/// <see cref="ISalesCustomerReturnService"/>. Sales records the customer's return invoice against a finalised sales invoice; it never
/// issues one. The route binds only the server context and the strict body; the service decides (authority first, replay, the
/// invoice, the calculator, one save). The draft routes' wire rules apply unchanged: exactly one canonical <c>Idempotency-Key</c>
/// (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400 <c>validation_failed</c>), 201 +
/// <c>Location</c>, the replay headers on the same key again (ADR-011 §4), another company's return 404, and every 401/403/404/503
/// code-less — except the finalisation's coded 503 <c>sales_customer_return_finance_port_unavailable</c> (SAT-D294: no Finance
/// reversal port is wired, so nothing is read, offered or written).
/// </summary>
public static partial class SalesEndpoints
{
    public const string CustomerReturnsPath = SalesCustomerReturnApiV1.CustomerReturnsPath;
    private static readonly HashSet<string> CustomerReturnListQueryNames = new(["invoiceId"], StringComparer.Ordinal);

    private static void MapCustomerReturnEndpoints(IEndpointRouteBuilder endpoints)
    {
        var returns = endpoints.MapGroup(CustomerReturnsPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        // SAT-D288: record a customer return invoice under `sales.returns.write` (+ the price read). The strict body; 201 with the
        // return. The amounts are copied from the original lines (SAT-D289 (1), SAT-D293 (3)); nothing in the body is an amount.
        returns.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesCustomerReturnService service,
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

            CreateSalesCustomerReturnRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesCustomerReturnJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.CreateCustomerReturnAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{CustomerReturnsPath}/{result.CustomerReturn.Id:D}";
                return Results.Json(result.CustomerReturn, SalesDraftJson.Options, statusCode: SalesCustomerReturnApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return CustomerReturnProblem(httpContext, exception, correlation);
            }
        })
            .WithName("CreateSalesCustomerReturnV1")
            .WithIdempotencyContract(SalesCustomerReturnApiV1.CreatedStatus)
            .Accepts<CreateSalesCustomerReturnRequest>("application/json")
            .Produces<SalesCustomerReturnView>(SalesCustomerReturnApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesCustomerReturnProblemCodesV1.DocumentDuplicate, SalesRuleCodes.CustomerReturnAmountsChanged,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", SalesCustomerReturnProblemCodesV1.InvoiceNotFound,
                SalesRuleCodes.CustomerReturnInvoiceNotFinalized, SalesRuleCodes.CustomerReturnShipmentNotOnInvoice,
                SalesRuleCodes.CustomerReturnLineNotOnInvoice, SalesRuleCodes.CustomerReturnQuantityInvalid,
                SalesRuleCodes.CustomerReturnQuantityExceeded, SalesCustomerReturnProblemCodesV1.UnshippedBalanceExceeded,
                SalesRuleCodes.CustomerReturnAmountOutOfRange, SalesRuleCodes.CustomerReturnDocumentInvalid,
                SalesRuleCodes.CustomerReturnLinesInvalid)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // The company's returns, optionally of one original invoice (`invoiceId`), newest first (technical bound
        // SalesCustomerReturnApiV1.MaximumListed), under `sales.returns.read` (+ the price read).
        returns.MapGet("/", async (
            [FromQuery] Guid? invoiceId,
            HttpContext httpContext,
            [FromServices] ISalesCustomerReturnService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, CustomerReturnListQueryNames, Array.Empty<string>())
                || invoiceId == Guid.Empty)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.ListCustomerReturnsAsync(invoiceId, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return CustomerReturnProblem(httpContext, exception, correlation);
            }
        })
            .WithName("GetSalesCustomerReturnsV1")
            .Produces<SalesCustomerReturnListResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        returns.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesCustomerReturnService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetCustomerReturnAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return CustomerReturnProblem(httpContext, exception, correlation);
            }
        })
            .WithName("GetSalesCustomerReturnV1")
            .Produces<SalesCustomerReturnView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-D290 (2): the preparer's submission under `sales.returns.write`.
        MapCustomerReturnStep(returns, "submission", "SubmitSalesCustomerReturnV1", finalizes: false, withReason: false,
            (service, id, body, metadata, cancellationToken) => service.SubmitCustomerReturnAsync(id, body, metadata, cancellationToken));

        // SAT-D290 (3): the approval under `sales.returns.approve`, never by the preparer or the submitter (recorded 409).
        MapCustomerReturnStep(returns, "approval", "ApproveSalesCustomerReturnV1", finalizes: false, withReason: false,
            (service, id, body, metadata, cancellationToken) => service.ApproveCustomerReturnAsync(id, body, metadata, cancellationToken));

        // SAT-D290 (2, 3): the rejection of a pending return under `sales.returns.approve`, never by the preparer or the submitter
        // (recorded 409), with an optional reason (422 when too long). Rejected is terminal.
        MapCustomerReturnStep(returns, "rejection", "RejectSalesCustomerReturnV1", finalizes: false, withReason: true,
            (service, id, body, metadata, cancellationToken) => service.RejectCustomerReturnAsync(id, body, metadata, cancellationToken));

        // SAT-D290 (2): the cancellation of a draft or pending return under `sales.returns.write`, with an optional reason. An approved
        // return is not cancelled here (its reversal needs the Finance port, SAT-D294).
        MapCustomerReturnStep(returns, "cancellation", "CancelSalesCustomerReturnV1", finalizes: false, withReason: true,
            (service, id, body, metadata, cancellationToken) => service.CancelCustomerReturnAsync(id, body, metadata, cancellationToken));

        // SAT-D291, SAT-D294: the finalisation under `sales.returns.finalize`; Finance admits the reversal before the commit.
        MapCustomerReturnStep(returns, "finalization", "FinalizeSalesCustomerReturnV1", finalizes: true, withReason: false,
            (service, id, body, metadata, cancellationToken) => service.FinalizeCustomerReturnAsync(id, body, metadata, cancellationToken));
    }

    private static void MapCustomerReturnStep(RouteGroupBuilder returns, string segment, string name, bool finalizes, bool withReason,
        Func<ISalesCustomerReturnService, Guid, SalesDraftStateCommandRequest, SalesCommandMetadata, CancellationToken,
            Task<SalesCustomerReturnCommandResult>> command)
    {
        var route = returns.MapPost($"/{{id:guid}}/{segment}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesCustomerReturnService service,
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
                var json = await reader.ReadToEndAsync(cancellationToken);
                body = withReason ? SalesCustomerReturnJson.ReadDecisionCommand(json) : SalesCustomerReturnJson.ReadStateCommand(json);
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await command(service, id, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                return Results.Json(result.CustomerReturn, SalesDraftJson.Options, statusCode: SalesCustomerReturnApiV1.CommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return CustomerReturnProblem(httpContext, exception, correlation);
            }
        })
            .WithName(name)
            .WithIdempotencyContract(SalesCustomerReturnApiV1.CommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesCustomerReturnView>(SalesCustomerReturnApiV1.CommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound();

        if (finalizes)
        {
            // SAT-D292 (6): the finalised returns of the invoice changed since approval (409, not recorded); Finance refused (422).
            route.ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                    SalesRuleCodes.CustomerReturnAmountsChanged, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                    SalesDraftProblemCodesV1.ConcurrentChange)
                .ProducesErpUnprocessableEntity(SalesCustomerReturnProblemCodesV1.FinanceRefused,
                    SalesRuleCodes.CustomerReturnQuantityExceeded, SalesCustomerReturnProblemCodesV1.UnshippedBalanceExceeded,
                    SalesRuleCodes.CustomerReturnAmountOutOfRange)
                .ProducesErpServiceUnavailable(SalesCustomerReturnProblemCodesV1.FinancePortUnavailable);
        }
        else if (string.Equals(segment, "approval", StringComparison.Ordinal) || string.Equals(segment, "rejection", StringComparison.Ordinal))
        {
            // SAT-D290 (3): the approval and the rejection refuse the preparer or the submitter.
            route.ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                    SalesCustomerReturnProblemCodesV1.SelfApproval, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                    SalesDraftProblemCodesV1.ConcurrentChange)
                .ProducesErpServiceUnavailable();
        }
        else
        {
            route.ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                    SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
                .ProducesErpServiceUnavailable();
        }

        if (withReason)
        {
            // The optional reason is at most SalesCustomerReturn.MaximumReasonLength characters (422, not recorded).
            route.ProducesErpUnprocessableEntity(SalesDraftProblemCodesV1.ApprovalReasonTooLong);
        }

        route.ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The API-40 problem: a replayed refusal carries the replay headers; the missing Finance port is the one coded 503 (SAT-D294) so
    /// the screen can say why the finalisation is closed; every other 401/403/404/503 stays code-less.
    /// </summary>
    private static IResult CustomerReturnProblem(HttpContext httpContext, SalesDraftServiceException exception,
        ICorrelationContext correlation)
    {
        if (exception.Status == StatusCodes.Status503ServiceUnavailable
            && string.Equals(exception.Code, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable, StringComparison.Ordinal))
        {
            return DraftProblem(StatusCodes.Status503ServiceUnavailable, exception.Code, correlation);
        }

        return RequestProblem(httpContext, exception, correlation);
    }
}
