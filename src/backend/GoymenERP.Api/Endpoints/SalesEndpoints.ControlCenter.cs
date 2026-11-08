using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-37 (Satış hazırlık planı v1 §3.3, Kontrol Merkezi; SAT-D95, SAT-D86, SAT-D225/EK42): the read-only list of the sales
/// follow-ups that need a person. <see cref="ISalesControlCenterService"/> authorizes and measures; the route binds only the
/// server context and accepts no query. Nothing is written in any module.
/// </summary>
public static partial class SalesEndpoints
{
    public const string ControlCenterPath = SalesControlCenterApiV1.Path;

    private static void MapControlCenterEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ControlCenterPath, async (
            HttpContext httpContext,
            [FromServices] ISalesControlCenterService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count != 0)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetControlCenterAsync(cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesControlCenterV1")
            .Produces<SalesControlCenterResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}
