using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;

namespace GoymenERP.Api.Endpoints;

public static class LogisticsSubcontractDeliveryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    public static IEndpointRouteBuilder MapLogisticsSubcontractDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(LogisticsSubcontractDeliveryV1.Path).WithTags("Logistics").RequireAuthorization().RequireCompanyContext()
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata());
        group.MapGet("", ListAsync).RequireAuthorization(LogisticsPermissionManifestV1.InstructionsRead)
            .WithName("ListLogisticsSubcontractDeliveriesV1").Produces<LogisticsSubcontractListV1>()
            .ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapGet("/sales-lines", SalesLinesAsync).RequireAuthorization(LogisticsPermissionManifestV1.InstructionsCreate)
            .WithName("ListLogisticsSubcontractSalesLinesV1").Produces<LogisticsSubcontractSalesLinesV1>()
            .ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapGet("/candidates/{salesOrderId:guid}/{salesOrderLineId:guid}", CandidatesAsync).RequireAuthorization(LogisticsPermissionManifestV1.InstructionsCreate)
            .WithName("ListLogisticsSubcontractCandidatesV1").Produces<LogisticsSubcontractCandidatesV1>()
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpConflict(SourceConflict).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapGet("/{instructionId:guid}", ReadAsync).RequireAuthorization(LogisticsPermissionManifestV1.InstructionsRead)
            .WithName("GetLogisticsSubcontractDeliveryV1").Produces<LogisticsSubcontractInstructionV1>()
            .ProducesErpBadRequest().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        Command(group.MapPost("", CreateAsync).RequireAuthorization(LogisticsPermissionManifestV1.InstructionsCreate)
            .WithName("CreateLogisticsSubcontractDeliveryV1").Accepts<CreateSubcontractDeliveryV1>("application/json"));
        Command(group.MapPost("/{instructionId:guid}/ship", ShipAsync).RequireAuthorization(LogisticsPermissionManifestV1.ShipmentsRecord)
            .WithName("ShipLogisticsSubcontractDeliveryV1").Accepts<ShipSubcontractDeliveryV1>("application/json"));
        Command(group.MapPost("/{instructionId:guid}/lines/{lineId:guid}/loads", LoadAsync).RequireAuthorization(LogisticsPermissionManifestV1.LoadsRecord)
            .WithName("LoadLogisticsSubcontractDeliveryV1").Accepts<RecordLoadRequest>("application/json"));
        Command(group.MapPost("/{instructionId:guid}/lines/{lineId:guid}/plan-excess/{exceptionId:guid}/reserve", ReservePlanExcessAsync)
            .RequireAuthorization(LogisticsPermissionManifestV1.LoadsRecord).WithName("ReserveLogisticsSubcontractPlanExcessV1")
            .Accepts<ReserveLogisticsPlanExcessV1>("application/json"));
        Command(group.MapPost("/{instructionId:guid}/weighing-corrections", CorrectAsync).RequireAuthorization(LogisticsPermissionManifestV1.WeighingsCorrect)
            .WithName("CorrectLogisticsSubcontractWeighingV1").Accepts<CorrectSubcontractWeighingV1>("application/json"));
        return endpoints;
    }
    private static void Command(RouteHandlerBuilder route) => route.Produces<LogisticsSubcontractInstructionV1>().WithIdempotencyContract(200, 409)
        .ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType()
        .ProducesErpUnprocessableEntity(Invalid).ProducesErpConflict(SourceConflict, ActiveInvoice,
            LogisticsPlanExcessV1.ApprovalRequired, LogisticsPlanExcessV1.Conflict,
            LogisticsProblemCodesV1.SameKeyPayloadMismatch, LogisticsProblemCodesV1.ConcurrentChange, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded,
            "sales_order_line_busy", "logistics_shipment_risk_over_limit", "logistics_shipment_risk_credit_limit_not_set",
            "logistics_shipment_risk_unavailable", "logistics_shipment_risk_exception_used",
            "inventory_label_version_conflict", "inventory_allocation_version_conflict", "inventory_allocation_insufficient_free_stock",
            "inventory_subcontract_delivery_source_mismatch", "inventory_subcontract_delivery_owner_mismatch",
            "inventory_physical_stock_insufficient", "inventory_label_not_releasable", "inventory_allocation_insufficient",
            "inventory_definite_shipment_idempotency_conflict", "inventory_definite_shipment_concurrent_change")
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    private static Task<IResult> SalesLinesAsync(HttpContext http, [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        ReadResultAsync(http, host, async () =>
        {
            if (http.Request.Query.Any(pair => pair.Key != "cursor" || pair.Value.Count != 1) || http.Request.Query["cursor"].FirstOrDefault()?.Length > 8192) throw new JsonException();
            return await host!.SalesLinesAsync(http.Request.Query["cursor"].FirstOrDefault(), token);
        }, token);
    private static Task<IResult> CandidatesAsync(Guid salesOrderId, Guid salesOrderLineId, HttpContext http,
        [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) => ReadResultAsync(http, host, async () =>
        {
            if (http.Request.Query.Any(pair => pair.Key != "pageIndex" || pair.Value.Count != 1)) throw new JsonException();
            var value = http.Request.Query["pageIndex"].FirstOrDefault(); var page = 0;
            if (value is not null && (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page)
                || page < 0 || page.ToString(System.Globalization.CultureInfo.InvariantCulture) != value)) throw new JsonException();
            return await host!.CandidatesAsync(salesOrderId, salesOrderLineId, page, token);
        }, token);
    private static Task<IResult> ReadAsync(Guid instructionId, HttpContext http, [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        ReadResultAsync(http, host, async () => { if (http.Request.Query.Count != 0) throw new JsonException(); return await host!.ReadAsync(instructionId, token); }, token);
    private static Task<IResult> ListAsync(HttpContext http, [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        ReadResultAsync(http, host, async () =>
        {
            if (http.Request.Query.Any(pair => pair.Key != "cursor" || pair.Value.Count != 1) || http.Request.Query["cursor"].FirstOrDefault()?.Length > 8192) throw new JsonException();
            return await host!.ListAsync(http.Request.Query["cursor"].FirstOrDefault(), token);
        }, token);
    private static Task<IResult> CreateAsync(HttpContext http, ICorrelationContext correlation, [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        WriteAsync<CreateSubcontractDeliveryV1>(http, correlation, host, (body, metadata) => host!.CreateAsync(body, metadata, token), token);
    private static Task<IResult> ShipAsync(Guid instructionId, HttpContext http, ICorrelationContext correlation,
        [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        WriteAsync<ShipSubcontractDeliveryV1>(http, correlation, host, (body, metadata) => host!.ShipAsync(instructionId, body, metadata, token), token);
    private static Task<IResult> LoadAsync(Guid instructionId, Guid lineId, HttpContext http, ICorrelationContext correlation,
        [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        WriteAsync<RecordLoadRequest>(http, correlation, host, (body, metadata) => host!.LoadAsync(instructionId, lineId, body, metadata, token), token);
    private static Task<IResult> ReservePlanExcessAsync(Guid instructionId, Guid lineId, Guid exceptionId, HttpContext http, ICorrelationContext correlation,
        [FromServices] ILogisticsSubcontractDeliveryHostV1? host, [FromServices] ILogisticsSubcontractPlanExcessReservationHostV1? reservation,
        CancellationToken token) => reservation is null ? Task.FromResult<IResult>(Results.Problem(statusCode: 503))
            : WriteAsync<ReserveLogisticsPlanExcessV1>(http, correlation, host,
                (body, metadata) => reservation.ReservePlanExcessAsync(instructionId, lineId, exceptionId, body, metadata, token), token);
    private static Task<IResult> CorrectAsync(Guid instructionId, HttpContext http, ICorrelationContext correlation,
        [FromServices] ILogisticsSubcontractDeliveryHostV1? host, CancellationToken token) =>
        WriteAsync<CorrectSubcontractWeighingV1>(http, correlation, host, (body, metadata) => host!.CorrectWeighingAsync(instructionId, body, metadata, token), token);
    private static async Task<IResult> ReadResultAsync<T>(HttpContext http, ILogisticsSubcontractDeliveryHostV1? host, Func<Task<T>> read, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (host is null) return Results.Problem(statusCode: 503);
        try { return Results.Json(await read(), Json); }
        catch (JsonException) { return Results.Problem(statusCode: 400); }
        catch (LogisticsInstructionServiceException error) { return Problem(http, error); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Results.Problem(statusCode: 503); }
    }
    private static async Task<IResult> WriteAsync<T>(HttpContext http, ICorrelationContext correlation, ILogisticsSubcontractDeliveryHostV1? host,
        Func<T, LogisticsCommandMetadata, Task<LogisticsSubcontractInstructionV1>> action, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (host is null) return Results.Problem(statusCode: 503);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        var keys = http.Request.Headers["Idempotency-Key"];
        if (http.Request.Query.Count != 0 || !correlation.IsResolved || keys.Count != 1 || keys[0]?.Trim() is not { Length: > 0 and <= 128 } key
            || key.Contains(',') || key.Any(char.IsControl)) return Results.Problem(statusCode: 400);
        try
        {
            using var document = await JsonDocument.ParseAsync(http.Request.Body, new() { MaxDepth = 16 }, token);
            RejectDuplicates(document.RootElement);
            var body = document.RootElement.Deserialize<T>(Json) ?? throw new JsonException();
            var response = await action(body, new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            if (response.IsReplay) { http.Response.Headers["Idempotency-Replayed"] = "true"; http.Response.Headers["Idempotency-Original-Correlation-Id"] = response.OriginalCorrelationId; }
            return Results.Json(response, Json);
        }
        catch (JsonException) { return Results.Problem(statusCode: 400); }
        catch (LogisticsInstructionServiceException error) { return Problem(http, error); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return Results.Problem(statusCode: 503); }
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException(); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    private static IResult Problem(HttpContext http, LogisticsInstructionServiceException error)
    {
        if (error.IsReplay) { http.Response.Headers["Idempotency-Replayed"] = "true"; http.Response.Headers["Idempotency-Original-Correlation-Id"] = error.OriginalCorrelationId; }
        if (error.Status is 400 or 401 or 403 or 404 or 503) return Results.Problem(statusCode: error.Status);
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        var detail = LogisticsPhysicalStockRefusalDetailV1.IsValid(error.Status, error.Code, error.Detail)
            ? InventoryRuntime.InventoryPhysicalStockRefusalProblem.Apply(error.Status, error, extensions, error.Detail) : null;
        return Results.Problem(statusCode: error.Status, detail: detail, extensions: extensions);
    }
}
