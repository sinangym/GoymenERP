using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-03 → Sales (SAT-D93/D97/D140/D147/D158): the order's tax reads. <c>GET …/drafts/{id}/tax</c> — the order's buyer tax and every
/// line's tax snapshot as the Settings line tax port resolved it (the draft read AND the price read; SAT-D150) — and
/// <c>GET /api/v1/sales/tax-options?on=</c> — the Settings lists the order screens offer for a business date (the draft read).
/// <see cref="ISalesDraftTaxService"/> measures the names; the routes bind only the server context and the strict query.
/// </summary>
public static partial class SalesEndpoints
{
    private static readonly HashSet<string> TaxOptionsQueryNames = new(["on"], StringComparer.Ordinal);

    private static void MapDraftTax(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/tax", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftTaxService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftTaxAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftTaxV1")
            .Produces<SalesOrderDraftTaxResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static void MapTaxOptions(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SalesDraftTaxApiV1.TaxOptionsPath, async (
            [FromQuery] string? on,
            HttpContext httpContext,
            [FromServices] ISalesDraftTaxService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, TaxOptionsQueryNames, TaxOptionsQueryNames)
                || !SalesDraftTaxApiV1.TryParseOn(on, out var day))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetTaxOptionsAsync(day, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesTaxOptionsV1")
            .Produces<SalesTaxOptionsResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
