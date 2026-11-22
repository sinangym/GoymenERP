using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// W1-C (DALGA_PLANI_v2 1.7/1.8): record notes and mentions, tasks and the personal collaboration inbox. Commands take exactly
/// one Idempotency-Key (no surrounding whitespace, no comma, at most 128 characters) and a strict JSON body; a replay answers
/// with <c>Idempotency-Replayed</c> and the original correlation id. 401/403/404/503 are code-less.
/// </summary>
public static class WorkCollaborationEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapWorkCollaborationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work").WithTags("Work").RequireAuthorization().RequireCompanyContext();

        group.MapGet("/records/{recordType}/{recordId:guid}/notes", (string recordType, Guid recordId, bool? includePassive, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListNotesAsync(recordType, recordId, includePassive == true, token)))
            .WithName("GetWorkRecordNotesV1").Produces<WorkNotePageV1>().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        var addNote = group.MapPost("/records/{recordType}/{recordId:guid}/notes", (string recordType, Guid recordId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<AddWorkNoteRequestV1, WorkNoteResponseV1>(http, correlation, 201,
                (request, metadata) => service.AddNoteAsync(recordType, recordId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Note, token))
            .WithName("AddWorkRecordNoteV1").Accepts<AddWorkNoteRequestV1>("application/json").WithIdempotencyContract(201, 409)
            .Produces<WorkNoteViewV1>(201);
        Command(addNote, WorkCollaborationProblemCodesV1.MentionRefused, WorkCollaborationProblemCodesV1.CorrectedNoteInvalid)
            .ProducesErpConflict(WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        var passivate = group.MapPost("/notes/{noteId:guid}/passivation", (Guid noteId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<PassivateWorkNoteRequestV1, WorkNoteResponseV1>(http, correlation, 200,
                (request, metadata) => service.PassivateNoteAsync(noteId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Note, token))
            .WithName("PassivateWorkNoteV1").Accepts<PassivateWorkNoteRequestV1>("application/json").WithIdempotencyContract(200, 409)
            .Produces<WorkNoteViewV1>();
        Command(passivate).ProducesErpConflict(WorkCollaborationProblemCodesV1.NoteNotOwn, WorkCollaborationProblemCodesV1.NoteAlreadyPassive,
            WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        // WP9 read receipt: idempotent by (note, reader), so no Idempotency-Key; the author's own note answers 409.
        group.MapPost("/notes/{noteId:guid}/read", (Guid noteId, HttpContext http, [FromServices] IWorkCollaborationServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.MarkNoteReadAsync(noteId, token)))
            .WithName("MarkWorkNoteReadV1").Produces<WorkNoteReadResponseV1>()
            .ProducesErpConflict(WorkCollaborationProblemCodesV1.NoteOwnRead)
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/tasks/mine", (bool? includeClosed, HttpContext http, [FromServices] IWorkCollaborationServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListMyTasksAsync(includeClosed == true, token)))
            .WithName("GetMyWorkTasksV1").Produces<WorkTaskPageV1>().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/records/{recordType}/{recordId:guid}/tasks", (string recordType, Guid recordId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListRecordTasksAsync(recordType, recordId, token)))
            .WithName("GetWorkRecordTasksV1").Produces<WorkTaskPageV1>().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        var create = group.MapPost("/tasks", (HttpContext http, [FromServices] IWorkCollaborationServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<CreateWorkTaskRequestV1, WorkTaskResponseV1>(http, correlation, 201,
                (request, metadata) => service.CreateTaskAsync(request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Task, token))
            .WithName("CreateWorkTaskV1").Accepts<CreateWorkTaskRequestV1>("application/json").WithIdempotencyContract(201, 409)
            .Produces<WorkTaskViewV1>(201);
        Command(create, WorkCollaborationProblemCodesV1.AssigneeRefused)
            .ProducesErpConflict(WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch, WorkCollaborationProblemCodesV1.ConcurrentChange);

        var complete = group.MapPost("/tasks/{taskId:guid}/completion", (Guid taskId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<CompleteWorkTaskRequestV1, WorkTaskResponseV1>(http, correlation, 200,
                (request, metadata) => service.CompleteTaskAsync(taskId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Task, token))
            .WithName("CompleteWorkTaskV1").Accepts<CompleteWorkTaskRequestV1>("application/json").WithIdempotencyContract(200, 409)
            .Produces<WorkTaskViewV1>();
        Command(complete).ProducesErpConflict(WorkCollaborationProblemCodesV1.VersionConflict, WorkCollaborationProblemCodesV1.TaskNotOpen,
            WorkCollaborationProblemCodesV1.TaskNotAssignee, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch,
            WorkCollaborationProblemCodesV1.ConcurrentChange);

        var cancel = group.MapPost("/tasks/{taskId:guid}/cancellation", (Guid taskId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<CancelWorkTaskRequestV1, WorkTaskResponseV1>(http, correlation, 200,
                (request, metadata) => service.CancelTaskAsync(taskId, request, metadata, token),
                response => (response.IsReplay, response.OriginalCorrelationId), response => response.Task, token))
            .WithName("CancelWorkTaskV1").Accepts<CancelWorkTaskRequestV1>("application/json").WithIdempotencyContract(200, 409)
            .Produces<WorkTaskViewV1>();
        Command(cancel).ProducesErpConflict(WorkCollaborationProblemCodesV1.VersionConflict, WorkCollaborationProblemCodesV1.TaskNotOpen,
            WorkCollaborationProblemCodesV1.TaskNotAssigner, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch,
            WorkCollaborationProblemCodesV1.ConcurrentChange);

        group.MapGet("/inbox-notifications", (string? notificationClass, bool? unreadOnly, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.ListMyInboxAsync(notificationClass, unreadOnly == true, token)))
            .WithName("GetMyWorkInboxNotificationsV1").Produces<WorkInboxNotificationPageV1>()
            .ProducesErpBadRequest(WorkCollaborationProblemCodesV1.ValidationFailed).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/inbox-notifications/{notificationId:guid}/read", (Guid notificationId, HttpContext http,
                [FromServices] IWorkCollaborationServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.MarkReadAsync(notificationId, token)))
            .WithName("MarkWorkInboxNotificationReadV1").Produces<WorkInboxNotificationReadResponseV1>()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        // WP9 / D7: work.tasks.assign or work.notes.write company-wide (code-less 403 otherwise); id + display name only.
        group.MapGet("/people", (string? query, HttpContext http, [FromServices] IWorkCollaborationServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) =>
            ReadAsync(http, correlation, () => service.SearchPeopleAsync(query, token)))
            .WithName("SearchWorkPeopleV1").Produces<WorkPeoplePageV1>()
            .ProducesErpBadRequest(WorkCollaborationProblemCodesV1.ValidationFailed).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static RouteHandlerBuilder Command(RouteHandlerBuilder route, params string[] unprocessable) => route
        .ProducesErpBadRequest(WorkCollaborationProblemCodesV1.ValidationFailed, WorkCollaborationProblemCodesV1.CommandMetadataInvalid)
        .AllowsEmptyErpBadRequestBody()
        .ProducesErpUnprocessableEntity([WorkCollaborationProblemCodesV1.ValidationFailed, .. unprocessable])
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
