using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class WorkDelegationEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapWorkDelegationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work/delegations").WithTags("Work").RequireAuthorization().RequireCompanyContext();
        group.MapGet("", async (bool? all, int? page, HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            {
                NoStore(http);
                try { return Results.Json(await service.ListAsync(all == true, page ?? 0, token)); }
                catch (WorkDelegationException error) { return Problem(error, http, correlation); }
            }).WithName("GetWorkDelegationsV1").Produces<WorkDelegationTableV1>()
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid).AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("", (HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            CommandAsync<CreateWorkDelegationV1>(http, correlation,
                (request, metadata) => service.CreateAsync(request, metadata, token), 201, token))
            .WithName("CreateWorkDelegationV1").Accepts<CreateWorkDelegationV1>("application/json")
            .WithIdempotencyContract(201, 409).Produces<WorkDelegationViewV1>(201)
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.Metadata).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity([WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.MissingPermission, WorkDelegationCodesV1.ScopeMismatch])
            .ProducesErpConflict(WorkDelegationCodesV1.Chain, WorkDelegationCodesV1.Concurrent, WorkDelegationCodesV1.KeyMismatch)
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/revocation", (Guid id, HttpContext http, ICorrelationContext correlation,
                [FromServices] IWorkDelegationServiceV1 service, CancellationToken token) =>
            CommandAsync<RevokeWorkDelegationV1>(http, correlation,
                (request, metadata) => service.RevokeAsync(id, request, metadata, token), 200, token))
            .WithName("RevokeWorkDelegationV1").Accepts<RevokeWorkDelegationV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<WorkDelegationViewV1>()
            .ProducesErpBadRequest(WorkDelegationCodesV1.Invalid, WorkDelegationCodesV1.Metadata).AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity([WorkDelegationCodesV1.Invalid])
            .ProducesErpConflict(WorkDelegationCodesV1.Version, WorkDelegationCodesV1.Concurrent, WorkDelegationCodesV1.KeyMismatch)
            .ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> CommandAsync<T>(HttpContext http, ICorrelationContext correlation,
        Func<T, WorkCommandMetadataV1, Task<WorkDelegationCommandResultV1>> execute, int status, CancellationToken token)
    {
        NoStore(http);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new(400, WorkDelegationCodesV1.Metadata), http, correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try { request = await JsonSerializer.DeserializeAsync<T>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new(400, WorkDelegationCodesV1.Invalid), http, correlation); }
        try
        {
            var answer = await execute(request, new(key, correlation.CorrelationId, correlation.CausationId));
            if (answer.Replayed)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = answer.OriginalCorrelationId;
            }
            return Results.Json(answer.Delegation, statusCode: status);
        }
        catch (WorkDelegationException error) { return Problem(error, http, correlation); }
    }

    private static IResult Problem(WorkDelegationException error, HttpContext http, ICorrelationContext correlation)
    {
        if (error.IsReplay && error.OriginalCorrelationId is { } original)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            http.Response.Headers["Idempotency-Original-Correlation-Id"] = original;
        }
        return error.Status is 401 or 403 or 404 or 503 ? Results.Problem(statusCode: error.Status)
            : Results.Problem(statusCode: error.Status, extensions: new Dictionary<string, object?>
                { ["code"] = error.Code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.Pragma = "no-cache";
    }
}
