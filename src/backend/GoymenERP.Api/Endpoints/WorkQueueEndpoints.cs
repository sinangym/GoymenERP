using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// B-25 Work queue: <c>GET /api/v1/work/queue</c> lists, read-only, the pending approvals of every connected source the
/// caller may see in the selected company. It adds no permission of its own: each source applies its owner's authority.
/// </summary>
public static class WorkQueueEndpoints
{
    public const string Path = "/api/v1/work/queue";
    private static readonly JsonSerializerOptions ContractJson = NativeApprovalJson.CreateOptions();

    public static IEndpointRouteBuilder MapWorkQueueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
                NativeApprovalQueueReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                httpContext.Response.Headers.Pragma = "no-cache";
                var result = await query.ListAsync(cancellationToken);
                return result.Outcome switch
                {
                    NativeApprovalReadOutcomeV1.Found when result.Resource is not null =>
                        Results.Json(result.Resource, ContractJson),
                    NativeApprovalReadOutcomeV1.Denied =>
                        Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                    NativeApprovalReadOutcomeV1.Unavailable =>
                        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => throw new InvalidOperationException(
                        "Work queue read returned an invalid outcome/resource combination."),
                };
            })
            .WithName("GetWorkQueueV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<NativeApprovalQueuePageV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }
}
