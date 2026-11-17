using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2; SAT-D309/D312, LOJ-D4/D9/D12): the subcontract service invoice draft
/// routes over <see cref="ISalesSubcontractServiceInvoiceService"/>. The client sends only the order and the delivery weighing
/// key; the service reads the net kg from the Logistics owner, the price basis from the order and the amounts from
/// SalesInvoiceCalculator. The invoice draft routes' wire rules apply unchanged: exactly one canonical <c>Idempotency-Key</c>
/// (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400 <c>validation_failed</c>), 201 +
/// <c>Location</c>, the replay headers on the same key again (ADR-011 §4), another company's order or invoice 404, and every
/// 401/403/404/503 code-less. A weighing that cannot be invoiced now is 409 <c>invoice_not_ready</c> with one field error per reason.
/// </summary>
public static partial class SalesEndpoints
{
    public const string SubcontractServiceInvoicesPath = SalesSubcontractServiceInvoiceApiV1.InvoicesPath;

    private static void MapSubcontractServiceInvoiceEndpoints(IEndpointRouteBuilder endpoints)
    {
        var invoices = endpoints.MapGroup(SubcontractServiceInvoicesPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();
        MapSubcontractInvoiceLifecycleEndpoints(invoices);

        invoices.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesSubcontractServiceInvoiceService service,
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

            CreateSalesSubcontractServiceInvoiceRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesSubcontractServiceInvoiceJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.CreateSubcontractServiceInvoiceAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{SubcontractServiceInvoicesPath}/{result.Invoice.Id:D}";
                return Results.Json(result.Invoice, SalesDraftJson.Options, statusCode: SalesSubcontractServiceInvoiceApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("CreateSalesSubcontractServiceInvoiceV1")
            .WithIdempotencyContract(SalesSubcontractServiceInvoiceApiV1.CreatedStatus)
            .Accepts<CreateSalesSubcontractServiceInvoiceRequest>("application/json")
            .Produces<SalesSubcontractServiceInvoiceDetailView>(SalesSubcontractServiceInvoiceApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesInvoiceProblemCodesV1.InvoiceNotReady, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        invoices.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesSubcontractServiceInvoiceService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetSubcontractServiceInvoiceAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesSubcontractServiceInvoiceV1")
            .Produces<SalesSubcontractServiceInvoiceDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
