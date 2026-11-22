using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP-C6 (DALGA_PLANI_v2 C.6; AI-D30): personal assistant memory. Own memory lives under <c>/assistant-memory/mine</c>; the
/// overseer surface under <c>/assistant-memory/oversight</c> needs <c>work.assistant-memory.oversee</c> and every view there is
/// recorded before it is served. Commands take exactly one Idempotency-Key (no surrounding whitespace, no comma, at most 128
/// characters) and a strict JSON body; a replay answers with <c>Idempotency-Replayed</c> and the original correlation id.
/// 401/403/404/503 are code-less. Responses are never cached.
/// </summary>
public static class WorkAssistantMemoryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapWorkAssistantMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work/assistant-memory").WithTags("Work").RequireAuthorization().RequireCompanyContext();

        group.MapGet("/mine/status", (HttpContext http, [FromServices] IWorkAssistantMemoryServiceV1 service, ICorrelationContext correlation,
                CancellationToken token) => ReadAsync(http, correlation, () => service.GetMyStatusAsync(token)))
            .WithName("GetMyWorkAssistantMemoryStatusV1").Produces<WorkAssistantMemoryStatusV1>().ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/mine/entries", (bool? includeInactive, HttpContext http, [FromServices] IWorkAssistantMemoryServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListMyEntriesAsync(includeInactive == true, token)))
            .WithName("GetMyWorkAssistantMemoryEntriesV1").Produces<WorkAssistantMemoryEntryPageV1>().ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/mine/oversight-views", (HttpContext http, [FromServices] IWorkAssistantMemoryServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListViewsOfMyMemoryAsync(token)))
            .WithName("GetMyWorkAssistantMemoryOversightViewsV1").Produces<WorkAssistantMemoryOversightViewPageV1>()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        var create = group.MapPost("/mine/entries", (HttpContext http, [FromServices] IWorkAssistantMemoryServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<CreateWorkAssistantMemoryEntryRequestV1, WorkAssistantMemoryEntryResponseV1>(http, correlation, 201,
                (request, metadata) => service.CreateEntryAsync(request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Entry, token))
            .WithName("CreateMyWorkAssistantMemoryEntryV1").Accepts<CreateWorkAssistantMemoryEntryRequestV1>("application/json")
            .WithIdempotencyContract(201, 409).Produces<WorkAssistantMemoryEntryViewV1>(201);
        Command(create).ProducesErpConflict(WorkAssistantMemoryProblemCodesV1.Disabled, WorkAssistantMemoryProblemCodesV1.ClosedForUser,
            WorkAssistantMemoryProblemCodesV1.LimitReached, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch,
            WorkCollaborationProblemCodesV1.ConcurrentChange);

        var revise = group.MapPost("/mine/entries/{entryId:guid}/revision", (Guid entryId, HttpContext http,
                [FromServices] IWorkAssistantMemoryServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ReviseWorkAssistantMemoryEntryRequestV1, WorkAssistantMemoryEntryResponseV1>(http, correlation, 200,
                (request, metadata) => service.ReviseEntryAsync(entryId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Entry, token))
            .WithName("ReviseMyWorkAssistantMemoryEntryV1").Accepts<ReviseWorkAssistantMemoryEntryRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkAssistantMemoryEntryViewV1>();
        Command(revise).ProducesErpConflict(WorkAssistantMemoryProblemCodesV1.Disabled, WorkAssistantMemoryProblemCodesV1.ClosedForUser,
            WorkAssistantMemoryProblemCodesV1.EntryNotActive, WorkCollaborationProblemCodesV1.VersionConflict,
            WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        var delete = group.MapPost("/mine/entries/{entryId:guid}/deletion", (Guid entryId, HttpContext http,
                [FromServices] IWorkAssistantMemoryServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<DeleteWorkAssistantMemoryEntryRequestV1, WorkAssistantMemoryDeletionResponseV1>(http, correlation, 200,
                (request, metadata) => service.DeleteEntryAsync(entryId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Deletion, token))
            .WithName("DeleteMyWorkAssistantMemoryEntryV1").Accepts<DeleteWorkAssistantMemoryEntryRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkAssistantMemoryDeletionViewV1>();
        Command(delete).ProducesErpConflict(WorkAssistantMemoryProblemCodesV1.EntryNotActive, WorkCollaborationProblemCodesV1.VersionConflict,
            WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        group.MapGet("/oversight/users/{userId:guid}/entries", (Guid userId, HttpContext http, [FromServices] IWorkAssistantMemoryServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ViewUserMemoryAsync(userId, token)))
            .WithName("ViewWorkAssistantMemoryOfUserV1").Produces<WorkAssistantMemoryOversightPageV1>()
            .ProducesErpBadRequest(WorkCollaborationProblemCodesV1.CommandMetadataInvalid).ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        var close = group.MapPost("/oversight/entries/{entryId:guid}/closure", (Guid entryId, HttpContext http,
                [FromServices] IWorkAssistantMemoryServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<CloseWorkAssistantMemoryEntryRequestV1, WorkAssistantMemoryEntryResponseV1>(http, correlation, 200,
                (request, metadata) => service.CloseEntryAsync(entryId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Entry, token))
            .WithName("CloseWorkAssistantMemoryEntryV1").Accepts<CloseWorkAssistantMemoryEntryRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkAssistantMemoryEntryViewV1>();
        Command(close).ProducesErpConflict(WorkAssistantMemoryProblemCodesV1.EntryNotActive, WorkCollaborationProblemCodesV1.VersionConflict,
            WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        var closure = group.MapPost("/oversight/users/{userId:guid}/closure", (Guid userId, HttpContext http,
                [FromServices] IWorkAssistantMemoryServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<SetWorkAssistantMemoryUserClosureRequestV1, WorkAssistantMemoryUserClosureResponseV1>(http, correlation, 200,
                (request, metadata) => service.SetUserClosureAsync(userId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Closure, token))
            .WithName("SetWorkAssistantMemoryUserClosureV1").Accepts<SetWorkAssistantMemoryUserClosureRequestV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkAssistantMemoryUserClosureViewV1>();
        Command(closure).ProducesErpConflict(WorkAssistantMemoryProblemCodesV1.UserClosureUnchanged, WorkCollaborationProblemCodesV1.VersionConflict,
            WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);
        return endpoints;
    }

    private static RouteHandlerBuilder Command(RouteHandlerBuilder route) => route
        .ProducesErpBadRequest(WorkCollaborationProblemCodesV1.ValidationFailed, WorkCollaborationProblemCodesV1.CommandMetadataInvalid)
        .AllowsEmptyErpBadRequestBody()
        .ProducesErpUnprocessableEntity([WorkCollaborationProblemCodesV1.ValidationFailed])
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    private static async Task<IResult> ReadAsync<T>(HttpContext http, ICorrelationContext correlation, Func<Task<T>> read)
    {
        NoStore(http.Response);
        try { return Results.Json(await read()); }
        catch (WorkCollaborationException exception) { return Problem(exception, correlation); }
    }

    private static async Task<IResult> CommandAsync<TRequest, TResponse>(HttpContext http, ICorrelationContext correlation, int status,
        Func<TRequest, WorkCommandMetadataV1, Task<TResponse>> command, Func<TResponse, (bool IsReplay, string OriginalCorrelationId)> replay,
        Func<TResponse, object> body, CancellationToken token)
    {
        NoStore(http.Response);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new(400, WorkCollaborationProblemCodesV1.CommandMetadataInvalid), correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        TRequest request;
        try { request = await JsonSerializer.DeserializeAsync<TRequest>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new(400, WorkCollaborationProblemCodesV1.ValidationFailed), correlation); }
        try
        {
            var result = await command(request, new(key, correlation.CorrelationId, correlation.CausationId));
            var (isReplay, original) = replay(result);
            if (isReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
            }

            return Results.Json(body(result), statusCode: status);
        }
        catch (WorkCollaborationException exception) { return Problem(exception, correlation, http); }
    }

    private static IResult Problem(WorkCollaborationException exception, ICorrelationContext correlation, HttpContext? http = null)
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
