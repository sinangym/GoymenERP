using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class LogisticsEndpoints
{
    public static IEndpointRouteBuilder MapLogisticsPlanExcessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/logistics").WithTags("Logistics").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/plan-excess", async (HttpContext http, [FromServices] ILogisticsPlanExcessServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Any(pair => pair.Key is not ("purpose" or "instructionId" or "cursor") || pair.Value.Count != 1) || http.Request.Query["cursor"].ToString().Length > 8192)
                return Problem(400, "validation_failed", correlation);
            var purpose = http.Request.Query["purpose"].ToString();
            var text = http.Request.Query["instructionId"].ToString(); Guid? instruction = null;
            if (text.Length > 0)
            {
                if (!Guid.TryParse(text, out var id) || id == Guid.Empty) return Problem(400, "validation_failed", correlation);
                instruction = id;
            }
            try { return Results.Json(await service.ListAsync(purpose, instruction, http.Request.Query["cursor"].FirstOrDefault(), token), LogisticsJson.Options); }
            catch (LogisticsInstructionServiceException error) { return Problem(http, error, correlation); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { return Problem(503, LogisticsPlanExcessV1.Unavailable, correlation); }
        }).WithName("ListLogisticsPlanExcessExceptionsV1").Produces<LogisticsPlanExcessPageV1>()
            .ProducesErpBadRequest("validation_failed", "invalid_cursor").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("/loading-instructions/{id:guid}/lines/{lineId:guid}/plan-excess", (Guid id, Guid lineId, HttpContext http,
            [FromServices] ILogisticsPlanExcessServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
                PlanExcessCommandAsync<ProposeLogisticsPlanExcessV1>(http, correlation,
                    (request, metadata) => service.ProposeAsync(id, lineId, request, metadata, token), token))
            .WithName("ProposeLogisticsPlanExcessV1").Accepts<ProposeLogisticsPlanExcessV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<LogisticsPlanExcessResultV1>()
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.CommandMetadataInvalid).ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsPlanExcessV1.Conflict, LogisticsStoreCodes.SameKeyPayloadMismatch, LogisticsStoreCodes.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("/plan-excess/{exceptionId:guid}/approve", (Guid exceptionId, HttpContext http,
            [FromServices] ILogisticsPlanExcessServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
                PlanExcessCommandAsync<ApproveLogisticsPlanExcessV1>(http, correlation,
                    (request, metadata) => service.ApproveAsync(exceptionId, request, metadata, token), token))
            .WithName("ApproveLogisticsPlanExcessV1").Accepts<ApproveLogisticsPlanExcessV1>("application/json")
            .WithIdempotencyContract(200, 409).Produces<LogisticsPlanExcessResultV1>()
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.CommandMetadataInvalid).ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsPlanExcessV1.Conflict, LogisticsStoreCodes.SameKeyPayloadMismatch, LogisticsStoreCodes.ConcurrentChange)
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }
    private static async Task<IResult> PlanExcessCommandAsync<T>(HttpContext http, ICorrelationContext correlation,
        Func<T, LogisticsCommandMetadata, Task<LogisticsPlanExcessResultV1>> command, CancellationToken token) where T : class
    {
        SetPrivateNoStore(http.Response);
        if (http.Request.Query.Count != 0 || !correlation.IsResolved || !TryReadIdempotencyKey(http.Request.Headers, out var key))
            return Problem(400, LogisticsProblemCodesV1.CommandMetadataInvalid, correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try
        {
            using var document = JsonDocument.Parse(await ReadBodyAsync(http, token));
            RejectPlanExcessDuplicateMembers(document.RootElement);
            request = document.RootElement.Deserialize<T>(LogisticsJson.Options) ?? throw new JsonException();
        }
        catch (JsonException) { return Problem(400, "validation_failed", correlation); }
        try
        {
            var answer = await command(request, Metadata(key, correlation));
            if (answer.IsReplay) ApplyReplayHeaders(http, answer.OriginalCorrelationId);
            return Results.Json(answer, LogisticsJson.Options);
        }
        catch (LogisticsInstructionServiceException error) { return Problem(http, error, correlation); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Problem(503, LogisticsPlanExcessV1.Unavailable, correlation); }
    }
    private static void RejectPlanExcessDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectPlanExcessDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectPlanExcessDuplicateMembers(item);
    }
}
