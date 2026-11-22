using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;

namespace GoymenERP.Api.Endpoints;

/// <summary>Problem codes of the Work inbox endpoints (W1-A).</summary>
public static class WorkInboxProblemCodesV1
{
    public const string RequestInvalid = "work_inbox_request_invalid";
    public const string ItemNotFound = "work_inbox_item_not_found";
}

/// <summary>
/// W1-A Work inbox. <c>GET /api/v1/work/inbox</c> lists, read-only, the approvals waiting for the caller's decision in
/// the selected company, with the permission they wait on, their duration and the owner's modelless explanation.
/// <c>POST /api/v1/work/inbox/events</c> records Work's own measurement event when the caller opens an item card or
/// follows the owner link. Neither endpoint decides anything and neither adds a permission: each source applies its
/// owner's approve permission, and the decision stays on the owner's screen.
/// </summary>
public static class WorkInboxEndpoints
{
    public const string Path = "/api/v1/work/inbox";
    public const string EventsPath = "/api/v1/work/inbox/events";
    private static readonly JsonSerializerOptions ContractJson = NativeApprovalJson.CreateOptions();

    public static IEndpointRouteBuilder MapWorkInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (
                string? view,
                string? kind,
                WorkInboxReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await query.ListAsync(view, kind, cancellationToken);
                return result.Outcome switch
                {
                    WorkInboxReadOutcomeV1.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
                    _ => Refusal(result.Outcome),
                };
            })
            .WithName("GetWorkInboxV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<WorkInboxPageV1>()
            .ProducesErpBadRequest(WorkInboxProblemCodesV1.RequestInvalid)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(EventsPath, async (
                RecordWorkInboxEventRequestV1 request,
                WorkInboxReadQueryV1 query,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await query.RecordEventAsync(request, cancellationToken);
                return result.Outcome switch
                {
                    WorkInboxReadOutcomeV1.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
                    _ => Refusal(result.Outcome),
                };
            })
            .WithName("RecordWorkInboxEventV1")
            .WithTags("Work")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Accepts<RecordWorkInboxEventRequestV1>("application/json")
            .Produces<WorkInboxEventRecordedV1>()
            .ProducesErpBadRequest(WorkInboxProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(WorkInboxProblemCodesV1.ItemNotFound)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }

    private static IResult Refusal(WorkInboxReadOutcomeV1 outcome) => outcome switch
    {
        WorkInboxReadOutcomeV1.Invalid => Coded(StatusCodes.Status400BadRequest, WorkInboxProblemCodesV1.RequestInvalid),
        WorkInboxReadOutcomeV1.NotFound => Coded(StatusCodes.Status404NotFound, WorkInboxProblemCodesV1.ItemNotFound),
        WorkInboxReadOutcomeV1.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        WorkInboxReadOutcomeV1.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Work inbox returned an invalid outcome/resource combination."),
    };

    private static IResult Coded(int status, string code) => Results.Problem(
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void SetPrivateNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
