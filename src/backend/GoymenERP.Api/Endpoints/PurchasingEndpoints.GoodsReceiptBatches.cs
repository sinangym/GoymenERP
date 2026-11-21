using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ALC-S16 (F-027; KLN-D41, KLN-D43, KLN-D45): the bulk goods receipt routes. POST submit → 201 + Location (posted, or waiting for
/// an approver when the kg difference is outside the Ayarlar tolerance or no tolerance is set); POST approval / rejection → 200 under
/// <c>purchasing.goods-receipts.approve-variance</c> (preparer ≠ approver); GET list / detail under the purchasing read permission;
/// GET label-code availability (the conflict check with the Ayarlar suffix suggestion) and GET label search (our code or the
/// supplier's original label number). The service decides; the route reads nothing but the server context and the wire.
/// </summary>
public static partial class PurchasingEndpoints
{
    private const string LabelCodeQueryName = "code";
    private const string LabelSearchTextQueryName = "text";
    private const string LimitQueryName = "limit";
    private const int DefaultBatchListLimit = 50;
    private static readonly HashSet<string> BatchListQueryNames = new([LimitQueryName], StringComparer.Ordinal);
    private static readonly HashSet<string> LabelSearchQueryNames = new([LabelSearchTextQueryName, LimitQueryName], StringComparer.Ordinal);

    private static void MapGoodsReceiptBatches(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/lines/{lineId:guid}/goods-receipt-batches", (Guid id, Guid lineId, HttpContext httpContext,
                [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            CreateAsync(httpContext, correlation, PurchaseGoodsReceiptBatchJsonV1.ReadSubmit,
                (body, metadata) => service.SubmitGoodsReceiptBatchAsync(id, lineId, body, metadata, cancellationToken),
                response => $"{OrdersPath}/{response.Batch.PurchaseOrderId:D}/goods-receipt-batches/{response.Batch.BatchId:D}",
                cancellationToken))
            .WithName("SubmitPurchaseGoodsReceiptBatchV1")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<SubmitPurchaseGoodsReceiptBatchV1>("application/json")
            .Produces<PurchaseGoodsReceiptBatchResponseV1>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid,
                InventoryGoodsReceiptCodesV1.InvalidRequest, InventoryGoodsReceiptCodesV1.InvalidVersion,
                InventoryGoodsReceiptCodesV1.InvalidQuantity)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SubmitConflictCodes())
            .ProducesErpUnprocessableEntity(PurchaseGoodsReceiptBatchCodesV1.LabelCodeRequired,
                PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel, "validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/goods-receipt-batches/{batchId:guid}/approval", (Guid id, Guid batchId, HttpContext httpContext,
                [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchaseGoodsReceiptBatchJsonV1.ReadDecision,
                (body, metadata) => service.ApproveGoodsReceiptBatchAsync(id, batchId, body, metadata, cancellationToken), cancellationToken))
            .WithName("ApprovePurchaseGoodsReceiptBatchV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<DecidePurchaseGoodsReceiptBatchV1>("application/json")
            .Produces<PurchaseGoodsReceiptBatchResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid,
                InventoryGoodsReceiptCodesV1.InvalidRequest, InventoryGoodsReceiptCodesV1.InvalidVersion,
                InventoryGoodsReceiptCodesV1.InvalidQuantity)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(ApprovalConflictCodes())
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/goods-receipt-batches/{batchId:guid}/rejection", (Guid id, Guid batchId, HttpContext httpContext,
                [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchaseGoodsReceiptBatchJsonV1.ReadDecision,
                (body, metadata) => service.RejectGoodsReceiptBatchAsync(id, batchId, body, metadata, cancellationToken), cancellationToken))
            .WithName("RejectPurchaseGoodsReceiptBatchV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<DecidePurchaseGoodsReceiptBatchV1>("application/json")
            .Produces<PurchaseGoodsReceiptBatchResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchaseGoodsReceiptBatchRuleCodes.NotPending,
                PurchaseGoodsReceiptBatchRuleCodes.SameActor, PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown,
                PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonRequired,
                PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonTooLong, "validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/goods-receipt-batches/{batchId:guid}", async (Guid id, Guid batchId, HttpContext httpContext,
            [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count != 0)
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            try { return Results.Json(await service.GetGoodsReceiptBatchAsync(id, batchId, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseGoodsReceiptBatchV1")
            .Produces<PurchaseGoodsReceiptBatchDetailV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/goods-receipt-batches", async (Guid id, HttpContext httpContext,
            [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadLimit(httpContext, BatchListQueryNames, DefaultBatchListLimit, out var limit))
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            try { return Results.Json(await service.GetGoodsReceiptBatchesAsync(id, limit, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseGoodsReceiptBatchesV1")
            .Produces<PurchaseGoodsReceiptBatchPageV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static void MapGoodsReceiptLabels(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PurchaseGoodsReceiptBatchApiV1.LabelCodeAvailabilityPath, async (HttpContext httpContext,
                [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var query = httpContext.Request.Query;
                if (query.Count != 1 || !query.TryGetValue(LabelCodeQueryName, out var values) || values.Count is 0
                    || values.Count > PurchaseGoodsReceiptBatchApiV1.MaximumCheckedCodes || values.Any(value => value is null))
                {
                    return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
                }

                try
                {
                    return Results.Json(await service.CheckGoodsReceiptLabelCodesAsync(values.Select(value => value!).ToArray(), cancellationToken),
                        PurchasingJson.Options);
                }
                catch (PurchasingServiceException exception)
                {
                    return Problem(httpContext, exception, correlation);
                }
            })
            .WithTags("Purchasing")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("CheckPurchaseGoodsReceiptLabelCodesV1")
            .Produces<PurchaseGoodsReceiptLabelCodeCheckV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(PurchaseGoodsReceiptBatchApiV1.LabelSearchPath, async (HttpContext httpContext,
                [FromServices] IPurchaseGoodsReceiptBatchServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var query = httpContext.Request.Query;
                if (!TryReadLimit(httpContext, LabelSearchQueryNames, DefaultBatchListLimit, out var limit)
                    || !query.TryGetValue(LabelSearchTextQueryName, out var text) || text.Count != 1 || text[0] is not { } searchText)
                {
                    return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
                }

                try
                {
                    return Results.Json(await service.SearchGoodsReceiptLabelsAsync(searchText, limit, cancellationToken), PurchasingJson.Options);
                }
                catch (PurchasingServiceException exception)
                {
                    return Problem(httpContext, exception, correlation);
                }
            })
            .WithTags("Purchasing")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("SearchPurchaseGoodsReceiptLabelsV1")
            .Produces<PurchaseGoodsReceiptLabelSearchPageV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>Only the allowed query names, each once; <c>limit</c> is a canonical 1..100 integer (absent = the default).</summary>
    private static bool TryReadLimit(HttpContext httpContext, HashSet<string> allowedNames, int defaultLimit, out int limit)
    {
        limit = defaultLimit;
        var query = httpContext.Request.Query;
        if (!query.All(pair => allowedNames.Contains(pair.Key) && pair.Value.Count == 1)) return false;
        if (!query.TryGetValue(LimitQueryName, out var values)) return true;
        return int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            && limit.ToString(CultureInfo.InvariantCulture) == values[0] && limit is >= 1 and <= 100;
    }

    private static string[] SubmitConflictCodes() =>
    [
        PurchasingProblemCodesV1.VersionConflict,
        PurchasingProblemCodesV1.SameKeyPayloadMismatch,
        PurchasingProblemCodesV1.ConcurrentChange,
        PurchasingProblemCodesV1.LineBusy,
        PurchaseAcceptanceParticipantCodes.OrderStateConflict,
        PurchaseGoodsReceiptBatchCodesV1.UnitUnsupported,
        PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining,
        PurchaseGoodsReceiptBatchCodesV1.VarianceAcknowledgementRequired,
        PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict,
        PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel,
        PurchaseGoodsReceiptBatchCodesV1.BatchIdTaken,
        .. ParticipantConflictCodes().Where(code => code != PurchaseAcceptanceParticipantCodes.OrderStateConflict),
    ];

    private static string[] ApprovalConflictCodes() =>
    [
        PurchasingProblemCodesV1.VersionConflict,
        PurchasingProblemCodesV1.SameKeyPayloadMismatch,
        PurchasingProblemCodesV1.ConcurrentChange,
        PurchasingProblemCodesV1.LineBusy,
        PurchaseGoodsReceiptBatchRuleCodes.NotPending,
        PurchaseGoodsReceiptBatchRuleCodes.SameActor,
        PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown,
        PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining,
        PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict,
        PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel,
        .. ParticipantConflictCodes(),
    ];

    /// <summary>The single-label receipt participants' 409s, unchanged (each label posts through them).</summary>
    private static string[] ParticipantConflictCodes() =>
    [
        PurchaseAcceptanceParticipantCodes.OrderVersionConflict,
        PurchaseAcceptanceParticipantCodes.ProductVersionConflict,
        PurchaseAcceptanceParticipantCodes.OrderStateConflict,
        PurchaseAcceptanceParticipantCodes.QuantityExceedsRemaining,
        PurchaseAcceptanceParticipantCodes.IdempotencyConflict,
        PurchaseAcceptanceParticipantCodes.ConcurrentChange,
        PurchaseAcceptanceParticipantCodes.LineBusy,
        SalesOrderLineSerializationLockContractV1.BusyCode,
        InventoryGoodsReceiptCodesV1.LotOrLabelConflict,
        InventoryGoodsReceiptCodesV1.IdempotencyConflict,
        InventoryGoodsReceiptCodesV1.ConcurrentChange,
    ];
}
