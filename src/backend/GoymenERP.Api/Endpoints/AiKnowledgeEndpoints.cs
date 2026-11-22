using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP-A4 (AI-D32 "onaylı karar dizini"): the packaged AI knowledge versions of this build and their human approval per company.
/// Every endpoint requires <c>ai.knowledge.approve</c> company-wide (registered, granted to no role). Commands take exactly one
/// Idempotency-Key (no surrounding whitespace, no comma, at most 128 characters) and a strict JSON body; a replay answers with
/// <c>Idempotency-Replayed</c> and the original correlation id. 401/403/404/503 are code-less.
/// </summary>
public static class AiKnowledgeEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapAiKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge").WithTags("Ai").RequireAuthorization().RequireCompanyContext();

        group.MapGet("/packages", (HttpContext http, [FromServices] IAiKnowledgeApprovalServiceV1 service, ICorrelationContext correlation,
                CancellationToken token) => ReadAsync(http, correlation, () => service.ListPackagesAsync(token)))
            .WithName("GetAiKnowledgePackagesV1").Produces<AiKnowledgePackageStatusPageV1>().ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        var approve = group.MapPost("/approvals", (HttpContext http, [FromServices] IAiKnowledgeApprovalServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ApproveAiKnowledgePackageRequestV1>(http, correlation, 201,
                (request, metadata) => service.ApproveAsync(request, metadata, token), token))
            .WithName("ApproveAiKnowledgePackageV1").Accepts<ApproveAiKnowledgePackageRequestV1>("application/json")
            .WithIdempotencyContract(201, 409).Produces<AiKnowledgeApprovalViewV1>(201);
        Command(approve, AiKnowledgeProblemCodesV1.PackageChanged)
            .ProducesErpConflict(AiKnowledgeProblemCodesV1.AlreadyApproved, AiKnowledgeProblemCodesV1.SameKeyPayloadMismatch,
                AiKnowledgeProblemCodesV1.ConcurrentChange);

        var withdraw = group.MapPost("/approvals/{approvalId:guid}/withdrawal", (Guid approvalId, HttpContext http,
                [FromServices] IAiKnowledgeApprovalServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<WithdrawAiKnowledgeApprovalRequestV1>(http, correlation, 200,
                (request, metadata) => service.WithdrawAsync(approvalId, request, metadata, token), token))
            .WithName("WithdrawAiKnowledgeApprovalV1").Accepts<WithdrawAiKnowledgeApprovalRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<AiKnowledgeApprovalViewV1>();
        Command(withdraw).ProducesErpConflict(AiKnowledgeProblemCodesV1.VersionConflict, AiKnowledgeProblemCodesV1.ApprovalNotActive,
            AiKnowledgeProblemCodesV1.SameKeyPayloadMismatch, AiKnowledgeProblemCodesV1.ConcurrentChange);
        return endpoints;
    }

    private static RouteHandlerBuilder Command(RouteHandlerBuilder route, params string[] unprocessable) => route
        .ProducesErpBadRequest(AiKnowledgeProblemCodesV1.ValidationFailed, AiKnowledgeProblemCodesV1.CommandMetadataInvalid)
        .AllowsEmptyErpBadRequestBody()
        .ProducesErpUnprocessableEntity([AiKnowledgeProblemCodesV1.ValidationFailed, .. unprocessable])
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    private static async Task<IResult> ReadAsync<T>(HttpContext http, ICorrelationContext correlation, Func<Task<T>> read)
    {
        NoStore(http.Response);
        try { return Results.Json(await read()); }
        catch (AiKnowledgeException exception) { return Problem(exception, correlation); }
    }

    private static async Task<IResult> CommandAsync<TRequest>(HttpContext http, ICorrelationContext correlation, int status,
        Func<TRequest, AiKnowledgeCommandMetadataV1, Task<AiKnowledgeApprovalResponseV1>> command, CancellationToken token)
    {
        NoStore(http.Response);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new(400, AiKnowledgeProblemCodesV1.CommandMetadataInvalid), correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        TRequest request;
        try { request = await JsonSerializer.DeserializeAsync<TRequest>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new(400, AiKnowledgeProblemCodesV1.ValidationFailed), correlation); }
        try
        {
            var result = await command(request, new(key, correlation.CorrelationId, correlation.CausationId));
            if (result.IsReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }

            return Results.Json(result.Approval, statusCode: status);
        }
        catch (AiKnowledgeException exception) { return Problem(exception, correlation, http); }
    }

    private static IResult Problem(AiKnowledgeException exception, ICorrelationContext correlation, HttpContext? http = null)
    {
        if (exception.IsReplay && http is not null && exception.OriginalCorrelationId is { } original)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }

        return exception.Status is 401 or 403 or 404 or 503 ? Results.Problem(statusCode: exception.Status)
            : Results.Problem(statusCode: exception.Status, extensions: new Dictionary<string, object?>
                { ["code"] = exception.Code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    }

    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
