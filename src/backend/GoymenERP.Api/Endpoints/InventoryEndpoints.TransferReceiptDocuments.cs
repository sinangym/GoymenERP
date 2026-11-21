using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryTransferReceiptDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapInventoryTransferReceiptWorkCandidatesEndpoint(endpoints);
        endpoints.MapGet(InventoryTransferApiV1.Path + "/{transferId:guid}/receipt-work", async (Guid transferId, HttpContext context,
            IInventoryTransferReceiptWorkV1 owner, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 1 || !context.Request.Query.TryGetValue("purpose", out var purpose)
                || purpose.Count != 1 || InventoryTransferReceiptWorkPurposesV1.Permission(purpose[0]) is null)
                return Results.Problem(statusCode: 400);
            try { return Results.Ok(await owner.ReadAsync(transferId, purpose[0]!, token)); }
            catch (OperationCanceledException) { throw; }
            catch (InventoryTransferException error) { return ReceiptDocumentProblem(error); }
        }).WithTags("Inventory").RequireAuthorization().RequireCompanyContext()
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata()).WithName("GetInventoryTransferReceiptWorkV1")
            .Produces<InventoryTransferReceiptWorkV1>().ProducesErpBadRequest(InventoryTransferCodesV1.ValidationFailed).AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        var group = endpoints.MapGroup(InventoryTransferReceiptDocumentV1.Path).WithTags("Inventory")
            .RequireCompanyContext().RequireAuthorization(InventoryPermissionManifestV1.StockTransferWrite)
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata());
        group.MapGet("/by-transfer/{transferId:guid}", async (Guid transferId, HttpContext context,
            IInventoryTransferReceiptDocumentsV1 owner, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Any(x => x.Key != "cursor" || x.Value.Count != 1)
                || context.Request.Query.TryGetValue("cursor", out var raw) && string.IsNullOrWhiteSpace(raw[0])) return Results.Problem(statusCode: 400);
            var cursor = context.Request.Query.TryGetValue("cursor", out var values) ? values[0] : null;
            try { return Results.Ok(await owner.ListAsync(transferId, cursor, token)); }
            catch (OperationCanceledException) { throw; }
            catch (InventoryTransferException error) { return ReceiptDocumentProblem(error); }
        }).WithName("ListInventoryTransferReceiptDocumentsV1").Produces<InventoryTransferReceiptDocumentsV1>()
            .ProducesErpBadRequest(InventoryTransferReceiptDocumentV1.Invalid).AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        Commands(group.MapPost("", async (HttpContext context, IInventoryTransferReceiptDocumentsV1 owner,
            ICurrentUser user, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            var read = await ReadTransferBodyAsync<CreateInventoryTransferReceiptDocumentV1>(context, correlation, token);
            if (read.Problem is not null) return read.Problem;
            try
            {
                var result = await owner.CreateAsync(read.Request!, StockCountTransport(user, correlation, read.Key,
                    InventoryPermissionManifestV1.StockTransferWrite), token);
                return DocumentResult(context, result, created: true);
            }
            catch (OperationCanceledException) { throw; }
            catch (InventoryTransferException error) { return ReceiptDocumentProblem(error); }
        }).WithName("CreateInventoryTransferReceiptDocumentV1").Accepts<CreateInventoryTransferReceiptDocumentV1>("application/json"))
            .Produces<InventoryTransferReceiptDocumentResultV1>(201);
        Commands(group.MapPost("/{documentId:guid}/finalize", async (Guid documentId, HttpContext context,
            IInventoryTransferReceiptDocumentsV1 owner, ICurrentUser user, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            var read = await ReadTransferBodyAsync<FinalizeInventoryTransferReceiptDocumentV1>(context, correlation, token);
            if (read.Problem is not null) return read.Problem;
            try
            {
                var result = await owner.FinalizeAsync(documentId, read.Request!, StockCountTransport(user, correlation, read.Key,
                    InventoryPermissionManifestV1.StockTransferWrite), token);
                return DocumentResult(context, result, created: false);
            }
            catch (OperationCanceledException) { throw; }
            catch (InventoryTransferException error) { return ReceiptDocumentProblem(error); }
        }).WithName("FinalizeInventoryTransferReceiptDocumentV1").Accepts<FinalizeInventoryTransferReceiptDocumentV1>("application/json"));
        return endpoints;

        static RouteHandlerBuilder Commands(RouteHandlerBuilder route) => route.WithIdempotencyContract(200)
            .Produces<InventoryTransferReceiptDocumentResultV1>()
            .ProducesErpBadRequest(InventoryTransferReceiptDocumentV1.Invalid, InventoryTransferCodesV1.ValidationFailed)
            .AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(InventoryTransferReceiptDocumentV1.VersionConflict, InventoryTransferReceiptDocumentV1.BindingConflict,
                InventoryTransferCodesV1.TransferVersionConflict, InventoryTransferCodesV1.TransferStateConflict, InventoryTransferCodesV1.IdempotencyConflict)
            .ProducesErpUnprocessableEntity(InventoryTransferReceiptCodesV1.OverageReasonRequired, InventoryTransferReceiptCodesV1.OverageReasonNotAllowed)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
    private static IResult DocumentResult(HttpContext context, InventoryTransferReceiptDocumentResultV1 result, bool created)
    {
        if (result.IsReplay)
        {
            context.Response.Headers["Idempotency-Replayed"] = "true";
            context.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
        }
        return created && !result.IsReplay ? Results.Created(InventoryTransferReceiptDocumentV1.Path + "/by-transfer/" + result.Document.TransferId, result)
            : Results.Ok(result);
    }
    private static IResult ReceiptDocumentProblem(InventoryTransferException error)
    {
        var code = error.Code;
        var safe = error.Status == 400 && code is InventoryTransferReceiptDocumentV1.Invalid or InventoryTransferCodesV1.ValidationFailed
            || error.Status == 409 && code is InventoryTransferReceiptDocumentV1.VersionConflict or InventoryTransferReceiptDocumentV1.BindingConflict
                or InventoryTransferCodesV1.TransferVersionConflict or InventoryTransferCodesV1.TransferStateConflict or InventoryTransferCodesV1.IdempotencyConflict
            || error.Status == 422 && code is InventoryTransferReceiptCodesV1.OverageReasonRequired or InventoryTransferReceiptCodesV1.OverageReasonNotAllowed;
        return safe ? Results.Problem(statusCode: error.Status, extensions: new Dictionary<string, object?> { ["code"] = code })
            : Results.Problem(statusCode: error.Status is 401 or 403 or 404 ? error.Status : 503);
    }
}
