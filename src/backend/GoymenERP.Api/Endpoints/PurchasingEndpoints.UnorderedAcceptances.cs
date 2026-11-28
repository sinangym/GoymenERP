using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    public const string UnorderedAcceptancesPath = "/api/v1/purchasing/unordered-acceptances";

    // Root composes this map with the existing Purchasing maps; no independent router/activation.
    private static void MapUnorderedAcceptances(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(UnorderedAcceptancesPath).WithTags("Purchasing")
            .RequireAuthorization().RequireCompanyContext();
        group.MapPost("", (HttpContext http, [FromServices] IPurchaseUnorderedAcceptanceServiceV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0) return Task.FromResult(Problem(400, "validation_failed", correlation));
            return CreateAsync<RecordPurchaseUnorderedAcceptanceV2, PurchaseUnorderedAcceptanceWriteV1>(http, correlation,
                PurchaseServiceOrderJsonV1.Read<RecordPurchaseUnorderedAcceptanceV2>,
                (request, metadata) => service.RecordOccurrenceAsync(request, metadata, token),
                response => $"{UnorderedAcceptancesPath}/{response.Record.Source.AcceptanceId:D}/lines/{response.Record.Source.AcceptanceLineId:D}", token);
        })
            .WithName("RecordPurchaseUnorderedAcceptanceV2")
            .Accepts<RecordPurchaseUnorderedAcceptanceV2>("application/json")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, 409)
            .Produces<PurchaseUnorderedAcceptanceWriteV1>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PurchasingProblemCodesV1.ConcurrentChange, PurchasingProblemCodesV1.SameKeyPayloadMismatch)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapGet("/{acceptanceId:guid}/lines/{acceptanceLineId:guid}", async (Guid acceptanceId, Guid acceptanceLineId,
            HttpContext http, [FromServices] PurchaseUnorderedAcceptanceReadHostV1 host,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await host.ReadAsync(acceptanceId, acceptanceLineId, token), PurchasingJson.Options); }
            catch (PurchasingServiceException error) { return Problem(http, error, correlation); }
        })
            .WithName("GetPurchaseUnorderedAcceptanceLineV1").Produces<PurchaseUnorderedAcceptanceSourceV1>()
            .ProducesErpBadRequest("validation_failed").ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}
