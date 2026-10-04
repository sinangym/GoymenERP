using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class LogisticsCustomerReturnEndpoints
{
    private const string Root = "/api/v1/logistics";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapLogisticsCustomerReturnEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Root).WithTags("Logistics").RequireAuthorization().RequireCompanyContext();

        group.MapGet("/customer-returns/{customerReturnId:guid}", async (Guid customerReturnId, HttpContext context,
            [FromServices] ILogisticsCustomerReturnServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            NoStore(context.Response);
            try
            {
                var result = await service.GetAsync(customerReturnId, token);
                return result is null ? Results.Problem(statusCode: 404) : Results.Json(result, LogisticsJson.Options);
            }
            catch (LogisticsCustomerReturnServiceException exception) { return Problem(exception, correlation, context.Response); }
        }).WithName("GetLogisticsCustomerReturnV1")
            .Produces<CustomerReturnViewV1>().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/orders/{salesOrderId:guid}/customer-returns", async (Guid salesOrderId, HttpContext context,
            [FromServices] ILogisticsCustomerReturnServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            NoStore(context.Response);
            try { return Results.Json(await service.ListOrderReturnsAsync(salesOrderId, token), LogisticsJson.Options); }
            catch (LogisticsCustomerReturnServiceException exception) { return Problem(exception, correlation, context.Response); }
        }).WithName("GetLogisticsOrderCustomerReturnsV1")
            .Produces<CustomerReturnViewV1[]>().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/instructions/{instructionId:guid}/lines/{lineId:guid}/customer-returns",
            (Guid instructionId, Guid lineId, HttpContext context, [FromServices] ILogisticsCustomerReturnServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) => CommandAsync<RequestCustomerReturnV1>(context, correlation,
                (request, metadata) => service.RequestAsync(instructionId, lineId, request, metadata, token), 201, token))
            .WithName("RequestLogisticsCustomerReturnV1").WithIdempotencyContract(201)
            .Accepts<RequestCustomerReturnV1>("application/json").Produces<CustomerReturnResponseV1>(201)
            .ProducesErpBadRequest("validation_failed", "command_metadata_invalid").AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsCustomerReturnCodesV1.VersionConflict,
                LogisticsCustomerReturnCodesV1.QuantityExceeded, "same_key_payload_mismatch", "concurrent_change")
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/customer-returns/{customerReturnId:guid}/approve",
            (Guid customerReturnId, HttpContext context, [FromServices] ILogisticsCustomerReturnServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) => CommandAsync<ApproveCustomerReturnV1>(context, correlation,
                (request, metadata) => service.ApproveAsync(customerReturnId, request, metadata, token), 200, token))
            .WithName("ApproveLogisticsCustomerReturnV1").WithIdempotencyContract(200)
            .Accepts<ApproveCustomerReturnV1>("application/json").Produces<CustomerReturnResponseV1>(200)
            .ProducesErpBadRequest("validation_failed", "command_metadata_invalid", InventoryCustomerReturnCodesV1.InvalidRequest,
                InventoryCustomerReturnCodesV1.InvalidQuantity).AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound().ProducesErpConflict(LogisticsCustomerReturnCodesV1.VersionConflict,
                LogisticsCustomerReturnCodesV1.QuantityExceeded, LogisticsCustomerReturnCodesV1.SourceInvalid,
                LogisticsCustomerReturnCodesV1.NotPending, LogisticsCustomerReturnCodesV1.OwnDecision,
                InventoryCustomerReturnCodesV1.ShipmentNotRecorded, InventoryCustomerReturnCodesV1.ReturnExceedsShipment,
                InventoryCustomerReturnCodesV1.IdempotencyConflict, InventoryCustomerReturnCodesV1.ConcurrentChange,
                "same_key_payload_mismatch", "concurrent_change")
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/customer-returns/{customerReturnId:guid}/reject",
            (Guid customerReturnId, HttpContext context, [FromServices] ILogisticsCustomerReturnServiceV1 service,
                ICorrelationContext correlation, CancellationToken token) => CommandAsync<RejectCustomerReturnV1>(context, correlation,
                (request, metadata) => service.RejectAsync(customerReturnId, request, metadata, token), 200, token))
            .WithName("RejectLogisticsCustomerReturnV1").WithIdempotencyContract(200)
            .Accepts<RejectCustomerReturnV1>("application/json").Produces<CustomerReturnResponseV1>(200)
            .ProducesErpBadRequest("validation_failed", "command_metadata_invalid").AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsCustomerReturnCodesV1.VersionConflict,
                LogisticsCustomerReturnCodesV1.NotPending, LogisticsCustomerReturnCodesV1.OwnDecision,
                "same_key_payload_mismatch", "concurrent_change")
            .ProducesErpUnprocessableEntity("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> CommandAsync<T>(HttpContext context, ICorrelationContext correlation,
        Func<T, LogisticsCommandMetadata, Task<CustomerReturnResponseV1>> command, int status, CancellationToken token)
    {
        NoStore(context.Response);
        if (!TryKey(context.Request.Headers, out var key) || !correlation.IsResolved)
            return Coded(400, "command_metadata_invalid", correlation);
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            request = JsonSerializer.Deserialize<T>(await reader.ReadToEndAsync(token), LogisticsJson.Options)
                ?? throw new JsonException();
        }
        catch (JsonException) { return Coded(400, "validation_failed", correlation); }

        try
        {
            var response = await command(request, new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow));
            if (response.IsReplay)
            {
                context.Response.Headers["Idempotency-Replayed"] = "true";
                context.Response.Headers["Idempotency-Original-Correlation-Id"] = response.OriginalCorrelationId;
            }
            return Results.Json(response, LogisticsJson.Options, statusCode: status);
        }
        catch (LogisticsCustomerReturnServiceException exception) { return Problem(exception, correlation, context.Response); }
        catch (LogisticsStoreException exception) { return Problem(Store(exception), correlation, context.Response); }
    }

    private static bool TryKey(IHeaderDictionary headers, out string key)
    {
        key = string.Empty;
        if (!headers.TryGetValue(IdempotencyHeader, out var values) || values.Count != 1 || values[0] is not { } value
            || string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim() || value.Contains(',')) return false;
        key = value;
        return true;
    }

    private static IResult Problem(LogisticsCustomerReturnServiceException exception, ICorrelationContext correlation,
        HttpResponse response)
    {
        if (exception.IsReplay && !string.IsNullOrWhiteSpace(exception.OriginalCorrelationId))
        {
            response.Headers["Idempotency-Replayed"] = "true";
            response.Headers["Idempotency-Original-Correlation-Id"] = exception.OriginalCorrelationId;
        }
        if (exception.Status is 401 or 403 or 404 or 503) return Results.Problem(statusCode: exception.Status);

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        };
        if (exception.Errors.Count > 0) extensions["errors"] = exception.Errors;
        if (exception.ExpectedVersion is not null)
            extensions["expectedVersion"] = exception.ExpectedVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (exception.CurrentVersion is not null)
            extensions["currentVersion"] = exception.CurrentVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(statusCode: exception.Status, extensions: extensions);
    }

    private static LogisticsCustomerReturnServiceException Store(LogisticsStoreException exception) => exception.Code switch
    {
        LogisticsStoreCodes.SameKeyPayloadMismatch => new(409, LogisticsProblemCodesV1.SameKeyPayloadMismatch),
        LogisticsStoreCodes.ConcurrentChange => new(409, LogisticsProblemCodesV1.ConcurrentChange),
        LogisticsStoreCodes.CompanyContextMismatch => new(403, "forbidden"),
        LogisticsStoreCodes.ValueDoesNotFit => new(422, "validation_failed"),
        LogisticsStoreCodes.AggregateLimitExceeded => new(409, LogisticsCustomerReturnCodesV1.QuantityExceeded),
        _ => new(503, "service_unavailable"),
    };

    private static IResult Coded(int status, string code, ICorrelationContext correlation) => Results.Problem(statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });

    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
