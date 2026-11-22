using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): <c>GET /api/v1/work/deadlines</c> lists, read-only, the upcoming deadlines every owner module lets
/// the caller see in the selected company (first owner: Settings' legal deadline calendar). An owner without a recorded
/// definition answers its deadlines as <c>undefined</c> with no counter. Nothing is decided or written; no permission is added.
/// </summary>
public static class WorkDeadlineEndpoints
{
    public const string Path = "/api/v1/work/deadlines";
    private static readonly JsonSerializerOptions ContractJson = NativeApprovalJson.CreateOptions();

    public static IEndpointRouteBuilder MapWorkDeadlineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (WorkDeadlineReadQueryV1 query, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                httpContext.Response.Headers.Pragma = "no-cache";
                if (httpContext.Request.Query.Count != 0)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                var result = await query.ListAsync(cancellationToken);
                return result.Outcome switch
                {
                    WorkDeadlineReadOutcomeV1.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
                    WorkDeadlineReadOutcomeV1.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                };
            })
            .WithName("GetWorkDeadlinesV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<WorkDeadlinePageV1>()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();
        return endpoints;
    }
}
