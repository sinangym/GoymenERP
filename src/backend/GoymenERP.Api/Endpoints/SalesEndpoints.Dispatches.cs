using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-30 / API-31 (Satış hazırlık planı v1 §3.3, MOD-06; SAT-D7, DEC-SAL-006, AT-025, AT-055): the two read-only Sales routes over
/// the Logistics dispatch owner — the sales-direction dispatch notes (S-07, <c>sales.drafts.read</c>) and the posted shares grouped
/// for a later invoice (S-08, <c>sales.invoices.read</c>). <see cref="ISalesDispatchReadService"/> measures the names; the route
/// binds only the server context and the strict query. Nothing is written in any module.
/// </summary>
public static partial class SalesEndpoints
{
    public const string DispatchesPath = SalesDispatchApiV1.DispatchesPath;
    public const string InvoiceCandidatesPath = SalesDispatchApiV1.InvoiceCandidatesPath;
    private const int MaximumDispatchStateLength = 16;
    private static readonly HashSet<string> DispatchListQueryNames = new(["pageSize", "cursor", "state"], StringComparer.Ordinal);
    private static readonly HashSet<string> InvoiceCandidateQueryNames = new(["companyAccountId"], StringComparer.Ordinal);
    public const string DispatchWeighingDifferencePath = SalesDispatchWeighingDifferenceApiV1.Path;
    private static readonly HashSet<string> NoQueryNames = new(StringComparer.Ordinal);

    private static void MapDispatchEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DispatchesPath, async (
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromQuery] string? state,
            HttpContext httpContext,
            [FromServices] ISalesDispatchReadService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, DispatchListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > SalesDispatchApiV1.MaximumPageSize || cursor is { Length: 0 or > MaximumCursorLength }
                || state is { Length: 0 or > MaximumDispatchStateLength })
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetDispatchesAsync(
                    new SalesDispatchListQuery(pageSize ?? SalesDispatchApiV1.DefaultPageSize, cursor, state), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesDispatchesV1")
            .Produces<SalesDispatchListResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(InvoiceCandidatesPath, async (
            [FromQuery] Guid? companyAccountId,
            HttpContext httpContext,
            [FromServices] ISalesDispatchReadService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, InvoiceCandidateQueryNames, Array.Empty<string>())
                || companyAccountId == Guid.Empty)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(companyAccountId),
                    cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesInvoiceCandidatesV1")
            .Produces<SalesInvoiceCandidatesResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SAT-S13 (SAT-D343, SAT-D319 (6), SAT-D389): one sales dispatch note's weighing-difference summary from the Logistics owner
        // (STK-S24) under the API-30 read name; no query, nothing written. A note without a record is 200 NotRecorded (the invoice
        // waits); an owner that cannot answer consistently is 503, never a zero.
        endpoints.MapGet(DispatchWeighingDifferencePath, async (
            Guid dispatchNoteId,
            HttpContext httpContext,
            [FromServices] ISalesDispatchWeighingDifferenceReadService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, NoQueryNames, Array.Empty<string>()) || dispatchNoteId == Guid.Empty)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetWeighingDifferenceAsync(dispatchNoteId, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesDispatchWeighingDifferenceV1")
            .Produces<SalesDispatchWeighingDifferenceView>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
