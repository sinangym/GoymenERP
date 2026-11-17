using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseAdditionalCostEndpointsV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/purchasing/additional-costs").WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        ServiceRead(group.MapGet("/sources/{kind:int}/{orderId:guid}", async (int kind, Guid orderId, HttpContext http,
            [FromServices] IPurchaseAdditionalCostServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            await ServiceReadAsync(http, correlation, () => service.SourceAsync((PurchaseAdditionalCostSourceKind)kind, orderId, token)))
            .WithName("GetPurchaseAdditionalCostSourceV1").Produces<PurchaseAdditionalCostSourceViewV1>());
        ServiceRead(group.MapGet("/sources/{kind:int}/{orderId:guid}/costs", async (int kind, Guid orderId, HttpContext http,
            [FromServices] IPurchaseAdditionalCostServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            await ServiceReadAsync(http, correlation, () => service.ListAsync((PurchaseAdditionalCostSourceKind)kind, orderId, token)))
            .WithName("ListPurchaseAdditionalCostsV1").Produces<IReadOnlyList<PurchaseAdditionalCostViewV1>>());
        ServiceRead(group.MapGet("/references", async (HttpContext http, [FromServices] IPurchaseAdditionalCostServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 1 || http.Request.Query["on"].Count != 1
                || !DateOnly.TryParseExact(http.Request.Query["on"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
                return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.ReferencesAsync(on, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Problem(503, "service_unavailable", correlation); }
        }).WithName("GetPurchaseAdditionalCostReferencesV1").Produces<PurchaseAdditionalCostReferencesV1>());
        group.MapPost("", async (HttpContext http, [FromServices] IPurchaseAdditionalCostServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key) || !correlation.IsResolved)
                return Problem(400, PurchasingProblemCodesV1.CommandMetadataInvalid, correlation);
            if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
            RecordPurchaseAdditionalCostV1 request;
            try { request = PurchaseServiceOrderJsonV1.Read<RecordPurchaseAdditionalCostV1>(await ReadBodyAsync(http, token)); }
            catch (JsonException) { return Problem(400, "validation_failed", correlation); }
            try
            {
                var result = await service.RecordAsync(request, Metadata(key, correlation), token);
                if (result.IsReplay) ApplyReplayHeaders(http, result.OriginalCorrelationId);
                return Results.Json(result, PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Problem(503, "service_unavailable", correlation); }
        }).WithName("RecordPurchaseAdditionalCostV1").Accepts<RecordPurchaseAdditionalCostV1>("application/json")
            .Produces<PurchaseAdditionalCostResponseV1>().WithIdempotencyContract(200, 409).ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid).AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType().ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpConflict("purchasing_additional_cost_source_version_conflict", "purchasing_additional_cost_type_version_conflict",
                "purchasing_additional_cost_tax_version_conflict",
                PurchasingProblemCodesV1.ConcurrentChange, PurchasingProblemCodesV1.SameKeyPayloadMismatch).ProducesErpServiceUnavailable();
        return endpoints;
    }
}
