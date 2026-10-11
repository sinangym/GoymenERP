using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkApprovalEndpoints
{
    public const string Path = "/api/v1/work/approvals";
    private static readonly JsonSerializerOptions ContractJson = NativeApprovalJson.CreateOptions();

    public static IEndpointRouteBuilder MapWorkApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
                [FromQuery] string? kind,
                [FromQuery] string? cursor,
                NativeApprovalReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadContract(httpContext.Request.Headers, out var includeInventoryLabelCode))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                SetContractResponseHeader(httpContext, includeInventoryLabelCode);
                return Read(await query.ListAsync(
                    kind, cursor, includeInventoryLabelCode, cancellationToken));
            })
            .WithName("GetWorkApprovalsV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<NativeApprovalReadPageV1>()
            .AllowsCodeLessErpBadRequestBody()
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
                if (!TryReadContract(httpContext.Request.Headers, out var includeInventoryLabelCode))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }
                SetContractResponseHeader(httpContext, includeInventoryLabelCode);
                return Read(await query.GetAsync(
                    kind, approvalId, includeInventoryLabelCode, cancellationToken));
            })
            .WithName("GetWorkApprovalV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<NativeApprovalReadDetailV1>()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }

    private static IResult Read<T>(NativeApprovalReadResultV1<T> result) where T : class => result.Outcome switch
    {
        NativeApprovalReadOutcomeV1.Found when result.Resource is not null =>
            Results.Json(result.Resource, ContractJson),
        NativeApprovalReadOutcomeV1.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        NativeApprovalReadOutcomeV1.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        NativeApprovalReadOutcomeV1.Unavailable =>
            Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Work approval read returned an invalid outcome/resource combination."),
    };

    private static void SetPrivateNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    private static bool TryReadContract(
        IHeaderDictionary headers,
        out bool includeInventoryLabelCode)
    {
        includeInventoryLabelCode = false;
        if (!headers.TryGetValue(NativeApprovalReadContractV1.Header, out var values))
        {
            return true;
        }

        if (values.Count != 1
            || !string.Equals(values[0],
                NativeApprovalReadContractV1.InventoryLabelDisplayVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        includeInventoryLabelCode = true;
        return true;
    }

    private static void SetContractResponseHeader(
        HttpContext httpContext,
        bool includeInventoryLabelCode)
    {
        if (includeInventoryLabelCode)
        {
            httpContext.Response.Headers[NativeApprovalReadContractV1.Header] =
                NativeApprovalReadContractV1.InventoryLabelDisplayVersion;
        }
    }
}
