using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkDecisionEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    public static IEndpointRouteBuilder MapWorkDecisionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work/decisions").WithTags("Work").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/{kind}/{id:guid}", async (string kind, Guid id, HttpContext http,
            [FromServices] WorkDecisionHostV1 host, CancellationToken token) =>
        {
            NoStore(http);
            try { return Results.Json(await host.PreviewAsync(kind, id, token)); }
            catch (WorkDecisionException error) { return Problem(http, error); }
        }).WithName("GetWorkDecisionV1").Produces<WorkDecisionPreviewV1>().ProducesErpAuthenticatedProblems()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        group.MapPost("/{kind}/{id:guid}", async (string kind, Guid id, HttpContext http,
            [FromServices] WorkDecisionHostV1 host, CancellationToken token) =>
        {
            NoStore(http);
            if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || keys[0] is not { Length: > 0 and <= 128 } key || key.Trim() != key || string.IsNullOrWhiteSpace(key) || key.Contains(','))
                return Problem(http, new(400, "work_decision_invalid"));
            if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
            WorkDecisionRequestV1? request;
            try { request = await JsonSerializer.DeserializeAsync<WorkDecisionRequestV1>(http.Request.Body, Json, token); }
            catch (JsonException) { return Problem(http, new(400, "work_decision_invalid")); }
            if (request is null) return Problem(http, new(400, "work_decision_invalid"));
            try
            {
                var answer = await host.DecideAsync(kind, id, request, key, token);
                Replay(http, answer.Replayed, answer.OriginalCorrelationId);
                return Results.Json(answer);
            }
            catch (WorkDecisionException error) { return Problem(http, error); }
            catch (InventoryOpeningCostException error)
            {
                // Only the owner's closed status/code set is translated; no second refusal is stored by Work.
                return Problem(http, new(error.Status, error.Code));
            }
        }).WithName("DecideWorkApprovalV1").Accepts<WorkDecisionRequestV1>("application/json")
            .WithIdempotencyContract(200, 409, 422).Produces<WorkDecisionResultV1>()
            .ProducesErpBadRequest("work_decision_invalid").AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpCodeLessConflict().ProducesErpUnprocessableEntity("work_decision_rejected")
            .ProducesErpUnsupportedMediaType().ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();
        return endpoints;
    }
    private static IResult Problem(HttpContext http, WorkDecisionException error)
    {
        Replay(http, error.Replayed, error.OriginalCorrelationId);
        // Owner problem vocabularies stay on the owner surface; Work exposes status only except its own body shape error.
        return error.Status is 400 or 422 ? Results.Problem(statusCode: error.Status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Status == 400 ? "work_decision_invalid" : "work_decision_rejected" }) : Results.Problem(statusCode: error.Status);
    }
    private static void Replay(HttpContext http, bool replay, string? original)
    {
        if (!replay || string.IsNullOrWhiteSpace(original)) return;
        http.Response.Headers["Idempotency-Replayed"] = "true";
        http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
    }
    private static void NoStore(HttpContext http) { http.Response.Headers.CacheControl = "private, no-store"; http.Response.Headers.Pragma = "no-cache"; }
}
