using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapReceiptReversalBatches(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/goods-receipts/reversal-batch", (Guid id, HttpContext httpContext,
                [FromServices] IPurchaseReceiptReversalBatchServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(httpContext, correlation, PurchaseReceiptReversalBatchJsonV1.Read,
                (body, metadata) => service.ReverseGoodsReceiptsAsync(id, body, metadata, token), token))
            .WithName("ReversePurchaseGoodsReceiptsV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<ReversePurchaseReceiptsV1>("application/json")
            .Produces<PurchaseReceiptReversalBatchResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid,
                InventoryGoodsReceiptReversalCodesV1.InvalidRequest)
            .AllowsEmptyErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventoryGoodsReceiptReversalCodesV1.AlreadyFulfilled, InventoryGoodsReceiptReversalCodesV1.ExceedsReceipt,
                InventoryGoodsReceiptReversalCodesV1.StockAllocated, InventoryGoodsReceiptReversalCodesV1.StockNotAvailable,
                InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict, InventoryGoodsReceiptCodesV1.IdempotencyConflict,
                InventoryGoodsReceiptCodesV1.ConcurrentChange, InventoryGoodsReceiptReversalCodesV1.ConcurrentChange,
                PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.ConcurrentChange, PurchasingStoreCodes.LineBusy,
                "sales_order_line_busy", PurchaseAcceptanceParticipantCodes.ProductVersionConflict, PurchasingProblemCodesV1.SameKeyPayloadMismatch)
            .ProducesErpUnprocessableEntity("validation_failed", InventoryGoodsReceiptReversalCodesV1.PartialQuantityNotRepresentable,
                PurchaseReceiptAdjustmentCodesV1.PieceCountRequired)
            .ProducesErpServiceUnavailable(PurchaseReceiptAdjustmentCodesV1.Unavailable, InventoryGoodsReceiptReversalCodesV1.Unavailable,
                InventoryGoodsReceiptReversalCodesV1.QuantityProfileUnavailable, PurchaseGoodsReceiptCodesV1.Unavailable,
                InventoryGoodsReceiptCodesV1.Unavailable, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable)
            .ProducesErpAuthenticatedProblems();
    }
}
