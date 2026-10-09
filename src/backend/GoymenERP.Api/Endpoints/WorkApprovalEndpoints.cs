using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkApprovalEndpoints
{
    public const string Path = "/api/v1/work/approvals";
    private static readonly JsonSerializerOptions ContractJson = FinanceJson.CreateOptions();

    public static IEndpointRouteBuilder MapWorkApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
                [FromQuery] string? cursor,
                NativeApprovalReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await query.ListAsync(cursor, cancellationToken));
            })
            .WithName("GetWorkApprovalsV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<NativeApprovalReadPageV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet($"{Path}/{{kind}}/{{approvalId:guid}}", async (
                string kind,
                Guid approvalId,
                NativeApprovalReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await query.GetAsync(kind, approvalId, cancellationToken));
            })
            .WithName("GetWorkApprovalV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<NativeApprovalReadDetailV1>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }

    private static IResult Read<T>(ReadResult<T> result) where T : class => result.Outcome switch
    {
        ReadOutcome.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
        ReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        ReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Work approval read returned an invalid outcome/resource combination."),
    };

    private static void SetPrivateNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
