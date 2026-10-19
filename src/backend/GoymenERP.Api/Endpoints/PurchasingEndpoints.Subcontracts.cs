using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapSubcontracts(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(PurchasingSubcontractApiV1.Path)
            .WithTags("Purchasing")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapPost("/", (HttpContext context, [FromServices] IPurchasingSubcontractRuntimeServiceV1 service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            CreateAsync(context, correlation, PurchasingSubcontractJsonV1.ReadCreate,
                (body, metadata) => service.CreateDraftAsync(body, metadata, cancellationToken),
                response => $"{PurchasingSubcontractApiV1.Path}/{response.Order.Id:D}", cancellationToken))
            .WithName("CreatePurchasingSubcontractDraftV1")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreatePurchasingSubcontractDraftV1>("application/json")
            .Produces<PurchasingSubcontractResponseV1>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PurchasingSubcontractProblemCodesV1.SameKeyPayloadMismatch,
                PurchasingSubcontractProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}", async (Guid id, HttpContext context,
            [FromServices] IPurchasingSubcontractRuntimeServiceV1 service,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response);
            try { return Results.Json(await service.GetAsync(id, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        })
            .WithName("GetPurchasingSubcontractV1")
            .Produces<PurchasingSubcontractViewV1>()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/admission", (Guid id, HttpContext context,
                [FromServices] IPurchasingSubcontractRuntimeServiceV1 service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            MutateAsync(context, correlation, PurchasingSubcontractJsonV1.ReadAdmit,
                (body, metadata) => service.AdmitAsync(id, body, metadata, cancellationToken), cancellationToken))
            .WithName("AdmitPurchasingSubcontractV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<AdmitPurchasingSubcontractV1>("application/json")
            .Produces<PurchasingSubcontractResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingSubcontractProblemCodesV1.VersionConflict,
                PurchasingSubcontractProblemCodesV1.NotDraft, PurchasingSubcontractProblemCodesV1.OwnerRefused,
                PurchasingSubcontractProblemCodesV1.SameKeyPayloadMismatch,
                PurchasingSubcontractProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}
