using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapReceiptReversals(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/goods-receipts/{acceptanceId:guid}/reversal", (Guid id, Guid acceptanceId, HttpContext httpContext,
                [FromServices] IPurchaseReceiptReversalServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(httpContext, correlation, PurchaseReceiptReversalJsonV1.Read,
                (body, metadata) => service.ReverseGoodsReceiptAsync(id, acceptanceId, body, metadata, token), token))
            .WithName("ReversePurchaseGoodsReceiptV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<ReversePurchaseReceiptV1>("application/json")
            .Produces<PurchaseReceiptReversalResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid,
                InventoryGoodsReceiptReversalCodesV1.InvalidRequest)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventoryGoodsReceiptReversalCodesV1.AlreadyFulfilled, InventoryGoodsReceiptReversalCodesV1.ExceedsReceipt,
                InventoryGoodsReceiptReversalCodesV1.StockAllocated, InventoryGoodsReceiptReversalCodesV1.StockNotAvailable,
                InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict, InventoryGoodsReceiptCodesV1.IdempotencyConflict,
                InventoryGoodsReceiptCodesV1.ConcurrentChange,
                InventoryGoodsReceiptReversalCodesV1.ConcurrentChange, PurchasingProblemCodesV1.VersionConflict,
                PurchasingProblemCodesV1.ConcurrentChange, PurchasingStoreCodes.LineBusy, "sales_order_line_busy",
                PurchaseAcceptanceParticipantCodes.ProductVersionConflict, PurchasingProblemCodesV1.SameKeyPayloadMismatch)
            .ProducesErpUnprocessableEntity("validation_failed", InventoryGoodsReceiptReversalCodesV1.PartialQuantityNotRepresentable)
            .ProducesErpServiceUnavailable(PurchaseReceiptAdjustmentCodesV1.Unavailable,
                InventoryGoodsReceiptReversalCodesV1.Unavailable, InventoryGoodsReceiptReversalCodesV1.QuantityProfileUnavailable,
                PurchaseGoodsReceiptCodesV1.Unavailable,
                InventoryGoodsReceiptCodesV1.Unavailable, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable)
            .ProducesErpAuthenticatedProblems();
    }
}
