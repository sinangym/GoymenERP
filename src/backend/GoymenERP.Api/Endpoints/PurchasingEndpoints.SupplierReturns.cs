using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): the supplier return routes. POST create → 201 + Location under
/// <c>purchasing.supplier-returns.record</c> (recorded and out of stock at the invoice unit cost, or waiting for a cost exception
/// approval when that cost cannot be read); POST cost-exception approval / rejection → 200 under <c>purchasing.orders.approve</c>
/// (SAT-D396); GET list / detail under the purchasing read permission. The service decides; the route reads nothing but the server
/// context and the wire.
/// </summary>
public static partial class PurchasingEndpoints
{
    private const int DefaultSupplierReturnListLimit = 50;
    private static readonly HashSet<string> SupplierReturnListQueryNames = new([LimitQueryName], StringComparer.Ordinal);
    private const string SourceLabelQueryName = "sourceLabelId";
    private const string QuantityKgQueryName = "quantityKg";
    private const string PieceCountQueryName = "pieceCount";
    private static readonly HashSet<string> SupplierReturnLineagePreviewQueryNames =
        new([SourceLabelQueryName, QuantityKgQueryName, PieceCountQueryName], StringComparer.Ordinal);

    private static void MapSupplierReturns(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/goods-receipts/{acceptanceId:guid}/supplier-return-cost-sources", async (Guid id, Guid acceptanceId,
            HttpContext httpContext, [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count != 0)
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            try { return Results.Json(await service.GetSupplierReturnCostSourcesAsync(id, acceptanceId, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseSupplierReturnCostSourcesV1")
            .Produces<PurchaseSupplierReturnCostSourcesV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventorySupplierReturnExitCodesV1.Status409Codes.Order(StringComparer.Ordinal).ToArray())
            .ProducesErpServiceUnavailable(SupplierReturnUnavailableCodes())
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/goods-receipts/{acceptanceId:guid}/supplier-return-lineage-preview", async (Guid id, Guid acceptanceId,
            HttpContext httpContext, [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            var query = httpContext.Request.Query;
            // AK-8b: exactly the documented names, each once; the label and the kg are required, the piece count optional.
            if (query.Keys.Any(name => !SupplierReturnLineagePreviewQueryNames.Contains(name)) || query.Any(pair => pair.Value.Count != 1)
                || !Guid.TryParseExact(query[SourceLabelQueryName].ToString(), "D", out var sourceLabelId)
                || !query.ContainsKey(QuantityKgQueryName)
                || query.ContainsKey(PieceCountQueryName) && (!int.TryParse(query[PieceCountQueryName].ToString(),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 1 || parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) != query[PieceCountQueryName].ToString()))
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            int? pieceCount = query.ContainsKey(PieceCountQueryName)
                ? int.Parse(query[PieceCountQueryName].ToString(), System.Globalization.CultureInfo.InvariantCulture) : null;
            try
            {
                return Results.Json(await service.GetSupplierReturnLineagePreviewAsync(id, acceptanceId, sourceLabelId,
                    query[QuantityKgQueryName].ToString(), pieceCount, cancellationToken), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseSupplierReturnLineagePreviewV1")
            .Produces<PurchaseSupplierReturnLineagePreviewV1>()
            .ProducesErpBadRequest(["validation_failed", .. InventorySupplierReturnExitCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)
            .ProducesErpUnprocessableEntity(PurchaseSupplierReturnCodesV1.PieceCountRequired)
            .ProducesErpServiceUnavailable(SupplierReturnUnavailableCodes())
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/supplier-returns", (Guid id, HttpContext httpContext,
                [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            CreateAsync(httpContext, correlation, PurchaseSupplierReturnJsonV1.ReadCreate,
                (body, metadata) => service.CreateSupplierReturnAsync(id, body, metadata, cancellationToken),
                response => $"{OrdersPath}/{response.Return.PurchaseOrderId:D}/supplier-returns/{response.Return.ReturnId:D}",
                cancellationToken))
            .WithName("CreatePurchaseSupplierReturnV1")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreatePurchaseSupplierReturnV1>("application/json")
            .Produces<PurchaseSupplierReturnResponseV1>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest(SupplierReturnBadRequestCodes())
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SupplierReturnCreateConflictCodes())
            .ProducesErpUnprocessableEntity(PurchaseSupplierReturnCodesV1.ReplacementUnsupported, PurchaseSupplierReturnCodesV1.PieceCountRequired,
                PurchaseSupplierReturnCodesV1.QuantityPriceUndecided, "validation_failed")
            .ProducesErpServiceUnavailable(SupplierReturnUnavailableCodes())
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/supplier-returns/{returnId:guid}/cost-exception/approval", (Guid id, Guid returnId,
                HttpContext httpContext, [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchaseSupplierReturnJsonV1.ReadDecision,
                (body, metadata) => service.ApproveSupplierReturnCostExceptionAsync(id, returnId, body, metadata, cancellationToken),
                cancellationToken))
            .WithName("ApprovePurchaseSupplierReturnCostExceptionV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<DecidePurchaseSupplierReturnV1>("application/json")
            .Produces<PurchaseSupplierReturnResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest(SupplierReturnBadRequestCodes())
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SupplierReturnApprovalConflictCodes())
            .ProducesErpUnprocessableEntity(PurchaseSupplierReturnCodesV1.PieceCountRequired, "validation_failed")
            .ProducesErpServiceUnavailable(SupplierReturnUnavailableCodes())
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/supplier-returns/{returnId:guid}/cost-exception/rejection", (Guid id, Guid returnId,
                HttpContext httpContext, [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchaseSupplierReturnJsonV1.ReadDecision,
                (body, metadata) => service.RejectSupplierReturnCostExceptionAsync(id, returnId, body, metadata, cancellationToken),
                cancellationToken))
            .WithName("RejectPurchaseSupplierReturnCostExceptionV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<DecidePurchaseSupplierReturnV1>("application/json")
            .Produces<PurchaseSupplierReturnResponseV1>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchaseSupplierReturnRuleCodes.NotPending,
                PurchaseSupplierReturnRuleCodes.SameActor, PurchaseSupplierReturnRuleCodes.MakerUnknown,
                PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity(PurchaseSupplierReturnRuleCodes.RejectionReasonRequired,
                PurchaseSupplierReturnRuleCodes.RejectionReasonTooLong, "validation_failed")
            .ProducesErpServiceUnavailable(PurchaseSupplierReturnCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/supplier-returns/{returnId:guid}", async (Guid id, Guid returnId, HttpContext httpContext,
            [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (httpContext.Request.Query.Count != 0)
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            try { return Results.Json(await service.GetSupplierReturnAsync(id, returnId, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseSupplierReturnV1")
            .Produces<PurchaseSupplierReturnDetailV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(PurchaseSupplierReturnCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/supplier-returns", async (Guid id, HttpContext httpContext,
            [FromServices] IPurchaseSupplierReturnServiceV1 service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadLimit(httpContext, SupplierReturnListQueryNames, DefaultSupplierReturnListLimit, out var limit))
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            try { return Results.Json(await service.GetSupplierReturnsAsync(id, limit, cancellationToken), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(httpContext, exception, correlation); }
        })
            .WithName("GetPurchaseSupplierReturnsV1")
            .Produces<PurchaseSupplierReturnPageV1>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(PurchaseSupplierReturnCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>The route's own 400s and Inventory's exit 400s (an exit is staged by create and by an approval).</summary>
    private static string[] SupplierReturnBadRequestCodes() =>
    [
        "validation_failed",
        PurchasingProblemCodesV1.CommandMetadataInvalid,
        .. InventorySupplierReturnExitCodesV1.Status400Codes.Order(StringComparer.Ordinal),
    ];

    private static string[] SupplierReturnCreateConflictCodes() =>
    [
        PurchasingProblemCodesV1.SameKeyPayloadMismatch,
        PurchasingProblemCodesV1.ConcurrentChange,
        PurchaseAcceptanceParticipantCodes.OrderStateConflict,
        PurchaseSupplierReturnCodesV1.ReturnIdTaken,
        PurchaseSupplierReturnCodesV1.InvoiceCostAvailable,
        PurchaseSupplierReturnCodesV1.CostRequired,
        PurchaseSupplierReturnCostAllocationContractV1.Required,
        PurchaseSupplierReturnCostAllocationContractV1.Stale,
        PurchaseSupplierReturnCostAllocationContractV1.ExceedsSource,
        PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
        PurchaseSupplierReturnCodesV1.LineagePartsMismatch,
        PurchaseSupplierReturnCodesV1.LineageSelectionRequired,
        PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded,
        PurchaseSupplierReturnCodesV1.QuantityAverageAvailable,
        PurchaseSupplierReturnCodesV1.QuantityManualCostDifference,
        PurchaseSupplierReturnCodesV1.QuantityPiecePriceDifference,
        .. InventorySupplierReturnExitCodesV1.Status409Codes.Order(StringComparer.Ordinal),
    ];

    private static string[] SupplierReturnApprovalConflictCodes() =>
    [
        PurchasingProblemCodesV1.VersionConflict,
        PurchasingProblemCodesV1.SameKeyPayloadMismatch,
        PurchasingProblemCodesV1.ConcurrentChange,
        PurchaseSupplierReturnRuleCodes.NotPending,
        PurchaseSupplierReturnRuleCodes.SameActor,
        PurchaseSupplierReturnRuleCodes.MakerUnknown,
        PurchaseSupplierReturnCostAllocationContractV1.Required,
        PurchaseSupplierReturnCostAllocationContractV1.Stale,
        PurchaseSupplierReturnCostAllocationContractV1.ExceedsSource,
        // SAT-D393 on a quantity return: Inventory can no longer take the approved manual cost (see the host mapping).
        PurchaseSupplierReturnCodesV1.CostRequired,
        .. InventorySupplierReturnExitCodesV1.Status409Codes.Order(StringComparer.Ordinal),
    ];

    private static string[] SupplierReturnUnavailableCodes() =>
    [
        PurchaseSupplierReturnCodesV1.Unavailable,
        PurchaseSupplierReturnCodesV1.InvoiceCostInvalid,
        PurchaseSupplierReturnCodesV1.LineageUnavailable,
        .. InventorySupplierReturnExitCodesV1.Status503Codes.Order(StringComparer.Ordinal),
    ];
}
