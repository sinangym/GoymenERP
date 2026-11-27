using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkMorningSummaryEndpoints
{
    public static IEndpointRouteBuilder MapWorkMorningSummaryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/work/morning-summary", (HttpContext http,
                [FromServices] IWorkMorningSummaryReadV1 summary, ICorrelationContext correlation, CancellationToken token) =>
            WorkCollaborationEndpoints.ReadAsync(http, correlation, () => summary.ReadAsync(token), noContentWhenNull: true))
            .WithTags("Work").WithName("GetMyWorkMorningSummaryV1").RequireAuthorization().RequireCompanyContext()
            .Produces<WorkMorningSummaryV1>().Produces(StatusCodes.Status204NoContent)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }
}
