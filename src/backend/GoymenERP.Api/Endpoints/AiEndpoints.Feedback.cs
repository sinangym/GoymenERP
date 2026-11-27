using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Ai.Feedback;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class AiEndpoints
{
    public const string AiFeedbackInvalidCode = "ai_feedback_invalid";
    public const string AiFeedbackVersionConflictCode = "ai_feedback_version_conflict";
    public const string AiFeedbackIdempotencyConflictCode = "ai_feedback_idempotency_conflict";
    private static readonly JsonSerializerOptions FeedbackJson = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };

    // Feedback records an observation; it never invokes an ERP command or creates a model tool.
    private static void MapFeedbackEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/answers/{answerId:guid}/feedback")
            .WithTags("Ai").RequireAuthorization().RequireCompanyContext();
        group.MapGet("", async (Guid answerId, HttpContext http, [FromServices] AiFeedbackServiceV1 service, CancellationToken token) =>
        {
            NoStore(http);
            var answer = await service.GetAsync(answerId, token);
            return answer.Status == AiFeedbackApiStatusV1.Succeeded && answer.Current is { } current
                ? Results.Json(current) : FeedbackProblem(answer.Status);
        }).WithName("GetAiAnswerFeedbackV1").Produces<AiFeedbackViewV1>()
            .ProducesErpBadRequest(AiFeedbackInvalidCode).AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();
        group.MapPost("", async (Guid answerId, HttpContext http, [FromServices] AiFeedbackServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            NoStore(http);
            if (!correlation.IsResolved) return FeedbackProblem(AiFeedbackApiStatusV1.Unavailable);
            if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
                || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key)
                || key.Trim() != key || key.Contains(',')) return FeedbackProblem(AiFeedbackApiStatusV1.Invalid);
            if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
            AiFeedbackRequestV1? request;
            try { request = await JsonSerializer.DeserializeAsync<AiFeedbackRequestV1>(http.Request.Body, FeedbackJson, token); }
            catch (JsonException) { return FeedbackProblem(AiFeedbackApiStatusV1.Invalid); }
            if (request is null) return FeedbackProblem(AiFeedbackApiStatusV1.Invalid);
            var answer = await service.RecordAsync(answerId, request, key, token);
            if (answer.Status != AiFeedbackApiStatusV1.Succeeded || answer.Receipt is not { } receipt)
                return FeedbackProblem(answer.Status);
            if (receipt.Replayed)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = receipt.OriginalCorrelationId;
            }
            return Results.Json(receipt);
        }).WithName("RecordAiAnswerFeedbackV1").Accepts<AiFeedbackRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<AiFeedbackReceiptViewV1>()
            .ProducesErpBadRequest(AiFeedbackInvalidCode).AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(AiFeedbackVersionConflictCode, AiFeedbackIdempotencyConflictCode)
            .ProducesErpCodeLessNotFound().ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();
    }
    private static IResult FeedbackProblem(AiFeedbackApiStatusV1 status) => status switch
    {
        AiFeedbackApiStatusV1.Invalid => Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = AiFeedbackInvalidCode }),
        AiFeedbackApiStatusV1.Refused => Results.Problem(statusCode: 403),
        AiFeedbackApiStatusV1.NotFound => Results.Problem(statusCode: 404),
        AiFeedbackApiStatusV1.VersionConflict => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = AiFeedbackVersionConflictCode }),
        AiFeedbackApiStatusV1.IdempotencyConflict => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = AiFeedbackIdempotencyConflictCode }),
        _ => Results.Problem(statusCode: 503),
    };
}
