using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseServiceOrderEndpointsV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/purchasing/service-orders").WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        ServiceRead(group.MapGet("/references", async (HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => await ServiceReadAsync(http, correlation, () => service.ReferencesAsync(token)))
            .WithName("GetPurchaseServiceOrderReferencesV1").Produces<PurchaseServiceOrderReferencesV1>());
        ServiceRead(group.MapGet("", async (HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => await ServiceReadAsync(http, correlation, () => service.ListAsync(token)))
            .WithName("ListPurchaseServiceOrdersV1").Produces<IReadOnlyList<PurchaseServiceOrderViewV1>>());
        ServiceRead(group.MapGet("/{id:guid}", async (Guid id, HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => await ServiceReadAsync(http, correlation, () => service.ReadAsync(id, token)))
            .WithName("GetPurchaseServiceOrderV1").Produces<PurchaseServiceOrderViewV1>());
        ServiceWrite(group.MapPost("", (HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => ServiceMutateAsync<CreatePurchaseServiceOrderV1>(http, correlation,
                (request, metadata) => service.CreateAsync(request, metadata, token), token))
            .WithName("CreatePurchaseServiceOrderV1").Accepts<CreatePurchaseServiceOrderV1>("application/json"));
        ServiceWrite(group.MapPost("/{id:guid}/approval", (Guid id, HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => ServiceMutateAsync<ApprovePurchaseServiceOrderV1>(http, correlation,
                (request, metadata) => service.ApproveAsync(id, request, metadata, token), token))
            .WithName("ApprovePurchaseServiceOrderV1").Accepts<ApprovePurchaseServiceOrderV1>("application/json"));
        ServiceWrite(group.MapPost("/{id:guid}/service-acceptances", (Guid id, HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => ServiceMutateAsync<AcceptPurchaseServiceV1>(http, correlation,
                (request, metadata) => service.AcceptAsync(id, request, metadata, token), token))
            .WithName("AcceptPurchaseServiceV1").Accepts<AcceptPurchaseServiceV1>("application/json"));
        ServiceWrite(group.MapPost("/{id:guid}/price-revisions", (Guid id, HttpContext http, [FromServices] IPurchaseServiceOrderServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) => ServiceMutateAsync<RevisePurchaseServiceOrderPricesV1>(http, correlation,
                (request, metadata) => service.RevisePricesAsync(id, request, metadata, token), token))
            .WithName("RevisePurchaseServiceOrderPricesV1").Accepts<RevisePurchaseServiceOrderPricesV1>("application/json"));
        return endpoints;
    }
    private static async Task<IResult> ServiceReadAsync<T>(HttpContext http, ICorrelationContext correlation, Func<Task<T>> action)
    {
        SetPrivateNoStore(http.Response);
        if (http.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
        try { return Results.Json(await action(), PurchasingJson.Options); }
        catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Problem(503, "service_unavailable", correlation); }
    }
    private static async Task<IResult> ServiceMutateAsync<T>(HttpContext http, ICorrelationContext correlation,
        Func<T, PurchasingCommandMetadata, Task<PurchaseServiceOrderResponseV1>> action, CancellationToken token) where T : class
    {
        SetPrivateNoStore(http.Response);
        if (!TryReadIdempotencyKey(http.Request.Headers, out var key) || !correlation.IsResolved)
            return Problem(400, PurchasingProblemCodesV1.CommandMetadataInvalid, correlation);
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
        T request;
        try { request = PurchaseServiceOrderJsonV1.Read<T>(await ReadBodyAsync(http, token)); }
        catch (JsonException) { return Problem(400, "validation_failed", correlation); }
        try
        {
            var response = await action(request, Metadata(key, correlation));
            if (response.IsReplay) ApplyReplayHeaders(http, response.OriginalCorrelationId);
            return Results.Json(response, PurchasingJson.Options);
        }
        catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Problem(503, "service_unavailable", correlation); }
    }
    private static void ServiceRead(RouteHandlerBuilder endpoint) => endpoint.ProducesErpBadRequest("validation_failed")
        .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    private static void ServiceWrite(RouteHandlerBuilder endpoint) => endpoint.WithIdempotencyContract(200, 409)
        .Produces<PurchaseServiceOrderResponseV1>().ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
        .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpConflict(PurchaseServiceOrder.VersionConflict, PurchaseServiceOrder.StateConflict, PurchaseServiceOrder.SameActor,
            PurchaseServiceOrder.AcceptanceExceeded, PurchasingProblemCodesV1.ConcurrentChange, PurchasingProblemCodesV1.SameKeyPayloadMismatch)
        .ProducesErpUnprocessableEntity("validation_failed", "purchasing_subcontract_own_material_required", "purchasing_subcontract_product_invalid")
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
public static class PurchaseServiceOrderJsonV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false,
      RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true };
    public static T Read<T>(string text) where T : class
    {
        using var doc = JsonDocument.Parse(text);
        RequireUnique(doc.RootElement);
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new JsonException();
    }
    private static void RequireUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Tekrarlı alan."); RequireUnique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RequireUnique(item);
    }
}
