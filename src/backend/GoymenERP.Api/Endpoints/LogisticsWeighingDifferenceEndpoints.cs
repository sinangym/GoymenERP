using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class LogisticsWeighingDifferenceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IEndpointRouteBuilder MapLogisticsWeighingDifferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/logistics").WithTags("Logistics").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/dispatch-notes/{dispatchNoteId:guid}/weighing-difference", async (Guid dispatchNoteId, HttpContext http,
            [FromServices] ILogisticsWeighingDifferenceServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            NoStore(http.Response);
            try { return Results.Json(LogisticsWeighingDifferenceWire.View(await service.GetByDispatchNoteAsync(dispatchNoteId, token))); }
            catch (LogisticsWeighingDifferenceServiceException exception) { return Problem(exception, correlation); }
        }).WithName("GetLogisticsWeighingDifferenceV1").Produces<DispatchKilogramsWire>()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        var record = group.MapPost("/weighing-differences", (HttpContext http,
            [FromServices] ILogisticsWeighingDifferenceServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<RecordDispatchKilogramsWire>(http, correlation, (request, metadata) => service.RecordAsync(LogisticsWeighingDifferenceWire.Command(request), metadata, token), token))
            .WithName("RecordLogisticsWeighingDifferenceV1").Accepts<RecordDispatchKilogramsWire>("application/json");
        Commands(record).ProducesErpConflict(LogisticsWeighingDifferenceCodesV1.DispatchNoteNotDraft,
            LogisticsWeighingDifferenceCodesV1.AlreadyRecorded, LogisticsWeighingDifferenceCodesV1.VersionConflict,
            WeighingDifferenceRuleCodes.UnitNotKilogram, WeighingDifferenceRuleCodes.MeasurementMissing,
            WeighingDifferenceRuleCodes.MeasurementUnexpected, WeighingDifferenceRuleCodes.MeasurementDuplicate,
            DispatchKilogramAllocation.InvalidSource, DispatchKilogramAllocation.InvalidDistribution, "same_key_payload_mismatch");

        var approve = group.MapPost("/weighing-differences/{recordId:guid}/approve", (Guid recordId, HttpContext http,
            [FromServices] ILogisticsWeighingDifferenceServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            CommandAsync<ApproveWeighingDifferenceV1>(http, correlation, (request, metadata) => service.ApproveAsync(recordId, request, metadata, token), token))
            .WithName("ApproveLogisticsWeighingDifferenceV1").Accepts<ApproveWeighingDifferenceV1>("application/json");
        Commands(approve).ProducesErpConflict(LogisticsWeighingDifferenceCodesV1.VersionConflict,
            WeighingDifferenceRuleCodes.NotPendingApproval, WeighingDifferenceRuleCodes.SelfApproval,
            WeighingDifferenceRuleCodes.EnteredByUnknown, "same_key_payload_mismatch");
        return endpoints;
    }

    private static RouteHandlerBuilder Commands(RouteHandlerBuilder route) => route.WithIdempotencyContract(200)
        .Produces<DispatchKilogramsResponseWire>().ProducesErpBadRequest("validation_failed", "command_metadata_invalid")
        .AllowsEmptyErpBadRequestBody().ProducesErpUnprocessableEntity("validation_failed")
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    private static async Task<IResult> CommandAsync<T>(HttpContext http, ICorrelationContext correlation,
        Func<T, LogisticsCommandMetadata, Task<WeighingDifferenceResponseV1>> command, CancellationToken token)
    {
        NoStore(http.Response);
        if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1
            || keys[0] is not { Length: > 0 and <= 128 } key || string.IsNullOrWhiteSpace(key) || key.Trim() != key || key.Contains(','))
            return Problem(new(400, "command_metadata_invalid"), correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try { request = await JsonSerializer.DeserializeAsync<T>(http.Request.Body, Json, token) ?? throw new JsonException(); }
        catch (JsonException) { return Problem(new(400, "validation_failed"), correlation); }
        try
        {
            var result = await command(request, new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            if (result.IsReplay)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return Results.Json(LogisticsWeighingDifferenceWire.Response(result));
        }
        catch (LogisticsWeighingDifferenceServiceException exception) { return Problem(exception, correlation); }
        catch (JsonException) { return Problem(new(400, "validation_failed"), correlation); }
    }

    private static IResult Problem(LogisticsWeighingDifferenceServiceException exception, ICorrelationContext correlation) =>
        exception.Status is 401 or 403 or 404 or 503 ? Results.Problem(statusCode: exception.Status)
        : Results.Problem(statusCode: exception.Status, extensions: new Dictionary<string, object?>
            { ["code"] = exception.Code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    private static void NoStore(HttpResponse response) { response.Headers.CacheControl = "private, no-store"; response.Headers.Pragma = "no-cache"; }
}
