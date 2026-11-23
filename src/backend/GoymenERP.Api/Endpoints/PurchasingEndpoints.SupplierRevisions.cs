using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapSupplierRevisions(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(OrdersPath).WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/{id:guid}/supplier-revisions", async (Guid id, HttpContext context,
            [FromServices] IPurchasingSupplierRevisionServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.GetSupplierRevisionsAsync(id, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("GetPurchaseOrderSupplierRevisionsV1").Produces<PurchaseSupplierRevisionHistoryView>()
            .ProducesErpBadRequest("validation_failed").ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        MapRevisionMutation(group.MapPost("/{id:guid}/supplier-revisions", (Guid id, HttpContext context,
            [FromServices] IPurchasingSupplierRevisionServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseSupplierRevisionJsonV1.ReadRevision,
                (body, metadata) => service.ReviseOrderSupplierAsync(id, body, metadata, token), token))
            .WithName("RevisePurchaseOrderSupplierV1").Accepts<RevisePurchaseOrderSupplierRequest>("application/json"),
            // F-017 §3.4 / ALS-D126: an active future allocation refuses an unconfirmed revision; a confirmed set must be the active set.
            PurchasingProblemCodesV1.LineHasAllocations, PurchasingProblemCodesV1.FutureAllocationSetChanged);
        MapRevisionMutation(group.MapPost("/{id:guid}/lines/{lineId:guid}/supplier-revision-price", (Guid id, Guid lineId, HttpContext context,
            [FromServices] IPurchasingSupplierRevisionServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseSupplierRevisionJsonV1.ReadPrice,
                (body, metadata) => service.SetSupplierRevisionPriceAsync(id, lineId, body, metadata, token), token))
            .WithName("SetPurchaseOrderSupplierRevisionPriceV1").Accepts<SetPurchaseSupplierRevisionPriceRequest>("application/json"));
    }

    private static void MapRevisionMutation(RouteHandlerBuilder endpoint, params string[] extraConflicts) => endpoint
        .WithIdempotencyContract(PurchasingApiV1.MutationStatus, 409).Produces<PurchaseOrderResponse>()
        .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid).AllowsEmptyErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpConflict([PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.SameKeyPayloadMismatch,
            PurchasingProblemCodesV1.ConcurrentChange, PurchaseOrder.SupplierRevisionBlocked, PurchaseOrder.SupplierRevisionNoChange,
            PurchaseOrder.SupplierRevisionNotDraft, PurchaseOrder.SupplierRevisionPriceRequired, .. extraConflicts])
        .ProducesErpUnprocessableEntity("validation_failed").ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
