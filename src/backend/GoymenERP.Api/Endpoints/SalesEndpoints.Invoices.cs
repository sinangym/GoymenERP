using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// S5 API-32 (Satış hazırlık planı v1 §3.3; Fatura çekirdeği 1): the sales invoice draft routes over
/// <see cref="ISalesInvoiceService"/>. The route binds only the server context and the strict body; the service decides
/// (authority first, replay, owners, calculator, one save). The draft routes' wire rules apply unchanged: exactly one canonical
/// <c>Idempotency-Key</c> (else 400 <c>command_metadata_invalid</c>), JSON only (415), the strict body (400
/// <c>validation_failed</c>), 201 + <c>Location</c>, the replay headers on the same key again (ADR-011 §4), another company's
/// invoice 404, and every 401/403/404/503 code-less. A share set that cannot be invoiced now is 409 <c>invoice_not_ready</c>
/// with one field error per reason.
/// </summary>
public static partial class SalesEndpoints
{
    public const string InvoicesPath = SalesInvoiceApiV1.InvoicesPath;
    private static readonly HashSet<string> InvoiceListQueryNames = new(["pageSize", "cursor"], StringComparer.Ordinal);

    private static void MapInvoiceEndpoints(IEndpointRouteBuilder endpoints)
    {
        var invoices = endpoints.MapGroup(InvoicesPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        invoices.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesInvoiceService service,
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

            CreateSalesInvoiceRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesInvoiceJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var result = await service.CreateInvoiceAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (result.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{InvoicesPath}/{result.Invoice.Id:D}";
                return Results.Json(result.Invoice, SalesDraftJson.Options, statusCode: SalesInvoiceApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("CreateSalesInvoiceV1")
            .WithIdempotencyContract(SalesInvoiceApiV1.CreatedStatus)
            .Accepts<CreateSalesInvoiceRequest>("application/json")
            .Produces<SalesInvoiceDetailView>(SalesInvoiceApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesInvoiceProblemCodesV1.InvoiceNotReady, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        invoices.MapGet("/", async (
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            [FromServices] ISalesInvoiceService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, InvoiceListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > SalesInvoiceApiV1.MaximumPageSize || cursor is { Length: 0 or > MaximumCursorLength })
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetInvoicesAsync(
                    new SalesInvoiceListQuery(pageSize ?? SalesInvoiceApiV1.DefaultPageSize, cursor), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesInvoicesV1")
            .Produces<SalesInvoiceListResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        invoices.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesInvoiceService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetInvoiceAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesInvoiceV1")
            .Produces<SalesInvoiceDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
