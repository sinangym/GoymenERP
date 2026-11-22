using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP1 (YOL-D17, YOL-D18): the versioned approval rule table shown in Ayarlar → Onay süreleri.
/// <c>GET /api/v1/work/approval-rules</c> lists one row per approval kind the inbox knows (owner module, the owner's approver
/// permission, duration class, escalation permission, version in force and its history). <c>PUT
/// /api/v1/work/approval-rules/{kind}</c> records a new version of one kind's rule with the expected version (<c>"0"</c> for
/// the decided default), exactly one Idempotency-Key and a strict JSON body; a replay answers with
/// <c>Idempotency-Replayed</c> and the original correlation id. Both need <c>settings.work-approval-duration.manage</c>
/// (company-wide); no permission is new. 401/403/404/503 are code-less.
/// </summary>
public static class WorkApprovalRuleEndpoints
{
    public const string Path = "/api/v1/work/approval-rules";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapWorkApprovalRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Work").RequireAuthorization().RequireCompanyContext();

        group.MapGet("", async (HttpContext http, [FromServices] IWorkApprovalRuleServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            {
                NoStore(http.Response);
                try { return Results.Json(await service.ListAsync(token)); }
                catch (WorkApprovalRuleException exception) { return Problem(exception, correlation); }
            })
            .WithName("GetWorkApprovalRulesV1")
            .Produces<WorkApprovalRuleTableV1>()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{kind}", (string kind, HttpContext http, [FromServices] IWorkApprovalRuleServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
                RecordAsync(kind, http, service, correlation, token))
            .WithName("RecordWorkApprovalRuleV1")
            .Accepts<RecordWorkApprovalRuleRequestV1>("application/json")
            .WithIdempotencyContract(200, 409)
            .Produces<WorkApprovalRuleViewV1>()
            .ProducesErpBadRequest(WorkApprovalRuleProblemCodesV1.ValidationFailed, WorkApprovalRuleProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity([WorkApprovalRuleProblemCodesV1.ValidationFailed])
            .ProducesErpConflict(WorkApprovalRuleProblemCodesV1.VersionConflict, WorkApprovalRuleProblemCodesV1.Unchanged,
                WorkApprovalRuleProblemCodesV1.SameKeyPayloadMismatch, WorkApprovalRuleProblemCodesV1.ConcurrentChange)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> RecordAsync(string kind, HttpContext http, IWorkApprovalRuleServiceV1 service,
        ICorrelationContext correlation, CancellationToken token)
    {
        NoStore(http.Response);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
        {
            return Problem(new(400, WorkApprovalRuleProblemCodesV1.CommandMetadataInvalid), correlation);
        }

        if (!http.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        RecordWorkApprovalRuleRequestV1 request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RecordWorkApprovalRuleRequestV1>(http.Request.Body, Json, token)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Problem(new(400, WorkApprovalRuleProblemCodesV1.ValidationFailed), correlation);
        }

        try
        {
            var result = await service.RecordAsync(kind, request,
                new(key, correlation.CorrelationId, correlation.CausationId), token);
            if (result.Replayed)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.CorrelationId;
            }

            return Results.Json(result.Rule);
        }
        catch (WorkApprovalRuleException exception)
        {
            return Problem(exception, correlation, http);
        }
    }

    private static IResult Problem(WorkApprovalRuleException exception, ICorrelationContext correlation, HttpContext? http = null)
    {
        if (exception.IsReplay && http is not null && exception.OriginalCorrelationId is { } original)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }

        return exception.Status is 401 or 403 or 404 or 503
            ? Results.Problem(statusCode: exception.Status)
            : Results.Problem(statusCode: exception.Status, extensions: new Dictionary<string, object?>
                { ["code"] = exception.Code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    }

    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
