using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class InventorySubcontractAllocationReleaseEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IEndpointRouteBuilder MapInventorySubcontractAllocationReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory/subcontract-allocations").WithTags("Inventory")
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite);
        group.MapGet("/release-candidates", async (string? cursor, HttpContext http,
            [FromServices] IInventorySubcontractAllocationReleaseServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
        {
            NoStore(http);
            if (http.Request.Query.Any(x => x.Key != "cursor" || x.Value.Count != 1)) return Problem(http, correlation, 400, "validation_failed");
            try { return Results.Json(await service.ListAsync(cursor, ct), Json); }
            catch (InventoryAllocationException error) { return Problem(http, correlation, error.Status, error.Code); }
        }).WithName("ListInventorySubcontractAllocationReleaseCandidatesV1").Produces<InventorySubcontractAllocationReleaseCandidatesV1>()
            .ProducesErpBadRequest("validation_failed", "invalid_cursor").ProducesErpServiceUnavailable(InventorySubcontractAllocationReleaseV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
        group.MapPost("/{allocationId:guid}/release-unused", async (Guid allocationId, HttpContext http,
            [FromServices] IInventorySubcontractAllocationReleaseServiceV1 service, ICorrelationContext correlation, CancellationToken ct) =>
        {
            NoStore(http);
            if (!correlation.IsResolved || !http.Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1
                || !InventorySubcontractDeliveryContractV1.Text(values[0], 128)) return Problem(http, correlation, 400, "validation_failed");
            if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
            try
            {
                using var document = await JsonDocument.ParseAsync(http.Request.Body, new JsonDocumentOptions { MaxDepth = 8 }, ct);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
                var properties = root.EnumerateObject().ToArray();
                if (properties.Length != 2 || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 2
                    || properties.Any(x => x.Name is not ("expectedAllocationVersion" or "reason") || x.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
                var result = await service.ReleaseAsync(allocationId,
                    new(root.GetProperty("expectedAllocationVersion").GetString()!, root.GetProperty("reason").GetString()!),
                    new("-", ErpPermissions.InventoryPhysicalAllocationWrite, [], correlation.CorrelationId, correlation.CausationId,
                        values[0]!, correlation.SourceChannel, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()), ct);
                if (result.IsReplay) Replay(http, result.OriginalCorrelationId);
                return Results.Json(result.Release, Json);
            }
            catch (JsonException) { return Problem(http, correlation, 400, "validation_failed"); }
            catch (InventoryAllocationException error)
            {
                if (error.IsReplay && error.OriginalCorrelationId is { } original) Replay(http, original);
                return Problem(http, correlation, error.Status, error.Code);
            }
        }).WithName("ReleaseUnusedInventorySubcontractAllocationV1").WithIdempotencyContract(200)
            .Accepts<ReleaseInventorySubcontractAllocationV1>("application/json").Produces<InventorySubcontractAllocationReleaseViewV1>()
            .ProducesErpBadRequest("validation_failed").AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventoryAllocationCodesV1.AllocationVersionConflict, InventoryAllocationCodesV1.AllocationStateConflict,
                InventoryAllocationCodesV1.IdempotencyConflict, InventoryAllocationCodesV1.ConcurrentChange, "sales_order_line_busy")
            .ProducesErpServiceUnavailable(InventorySubcontractAllocationReleaseV1.Unavailable).ProducesErpAuthenticatedProblems();
        return endpoints;
    }
    private static IResult Problem(HttpContext http, ICorrelationContext correlation, int status, string code)
    {
        NoStore(http);
        if (status == 404) return Results.NotFound();
        return Results.Problem(statusCode: status, extensions: new Dictionary<string, object?>
        { ["code"] = code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });
    }
    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "private, no-store";
    private static void Replay(HttpContext http, string correlation)
    { http.Response.Headers["Idempotency-Replayed"] = "true"; http.Response.Headers["Idempotency-Original-Correlation-Id"] = correlation; }
}
