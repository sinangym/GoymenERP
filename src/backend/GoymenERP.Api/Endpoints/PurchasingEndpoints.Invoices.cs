using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapIncomingInvoices(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(PurchaseIncomingInvoiceWireV1.Path).WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        InvoiceWorkspaceRead(group.MapGet("", async (string? officialNumber, int? pageIndex, HttpContext context,
            [FromServices] IPurchaseIncomingInvoiceWorkspaceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Keys.Any(key => key is not ("officialNumber" or "pageIndex"))) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.ListAsync(officialNumber, pageIndex ?? 0, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("ListPurchaseIncomingInvoicesV1").Produces<PurchaseIncomingInvoicePageV1>());
        InvoiceWorkspaceRead(group.MapGet("/{id:guid}/lines/{lineId:guid}/receipt-candidates", async (Guid id, Guid lineId, int? pageIndex,
            HttpContext context, [FromServices] IPurchaseIncomingInvoiceWorkspaceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Keys.Any(key => key != "pageIndex")) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.CandidatesAsync(id, lineId, pageIndex ?? 0, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("GetPurchaseInvoiceReceiptCandidatesV1").Produces<PurchaseInvoiceReceiptCandidatePageV1>());
        InvoiceWorkspaceRead(group.MapGet("/{id:guid}/lines/{lineId:guid}/additional-cost-candidates", async (Guid id, Guid lineId, int? pageIndex,
            HttpContext context, [FromServices] IPurchaseIncomingInvoiceWorkspaceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Keys.Any(key => key != "pageIndex") || context.Request.Query["pageIndex"].Count > 1)
                return Problem(400, "validation_failed", correlation);
            try { return Results.Json(await service.AdditionalCostCandidatesAsync(id, lineId, pageIndex ?? 0, token), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Problem(503, "service_unavailable", correlation); }
        }).WithName("GetPurchaseInvoiceAdditionalCostCandidatesV1").Produces<PurchaseInvoiceAdditionalCostCandidatePageV1>());
        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, [FromServices] IPurchaseIncomingInvoiceWorkflowV1 service,
            ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response);
            if (context.Request.Query.Count != 0) return Problem(400, "validation_failed", correlation);
            try { return Results.Json(PurchaseIncomingInvoiceWireV1.View(await service.ReadAsync(id, token)), PurchasingJson.Options); }
            catch (PurchasingServiceException exception) { return Problem(context, exception, correlation); }
        }).WithName("GetPurchaseIncomingInvoiceV1").Produces<PurchaseIncomingInvoiceViewV1>()
            .ProducesErpBadRequest("validation_failed").ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        InvoiceMutation(group.MapPost("/{id:guid}/matching", (Guid id, HttpContext context,
            [FromServices] IPurchaseIncomingInvoiceWorkflowV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseIncomingInvoiceWireV1.ReadMatch,
                async (request, metadata) => PurchaseIncomingInvoiceWireV1.Response(await service.MatchAsync(id, request, metadata, token)), token))
            .WithName("MatchPurchaseIncomingInvoiceV1").Accepts<PurchaseIncomingInvoiceMatchRequestV1>("application/json"));
        InvoiceMutation(group.MapPost("/{id:guid}/variance-approval", (Guid id, HttpContext context,
            [FromServices] IPurchaseIncomingInvoiceWorkflowV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseIncomingInvoiceWireV1.ReadApproval,
                async (request, metadata) => PurchaseIncomingInvoiceWireV1.Response(await service.ApproveVarianceAsync(id, request, metadata, token)), token))
            .WithName("ApprovePurchaseIncomingInvoiceVarianceV1").Accepts<PurchaseIncomingInvoiceVarianceApprovalRequestV1>("application/json"));
        InvoiceMutation(group.MapPost("/{id:guid}/finalization", (Guid id, HttpContext context,
            [FromServices] IPurchaseIncomingInvoiceWorkflowV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseIncomingInvoiceWireV1.ReadVersion,
                async (request, metadata) => PurchaseIncomingInvoiceWireV1.Response(await service.FinalizeAsync(id, request, metadata, token)), token))
            .WithName("FinalizePurchaseIncomingInvoiceV1").Accepts<PurchaseIncomingInvoiceVersionRequestV1>("application/json"));
        InvoiceMutation(group.MapPost("/{id:guid}/finance-admission", (Guid id, HttpContext context,
            [FromServices] IPurchaseIncomingInvoiceWorkflowV1 service, ICorrelationContext correlation, CancellationToken token) =>
            MutateAsync(context, correlation, PurchaseIncomingInvoiceWireV1.ReadVersion,
                async (request, metadata) => PurchaseIncomingInvoiceWireV1.Response(await service.RetryFinanceAsync(id, request, metadata, token)), token))
            .WithName("RetryPurchaseIncomingInvoiceFinanceAdmissionV1").Accepts<PurchaseIncomingInvoiceVersionRequestV1>("application/json"));
    }
    private static void InvoiceMutation(RouteHandlerBuilder endpoint) => endpoint
        .WithIdempotencyContract(PurchasingApiV1.MutationStatus, 409).Produces<PurchaseIncomingInvoiceCommandResponseV1>()
        .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid).AllowsEmptyErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.SameKeyPayloadMismatch,
            PurchasingProblemCodesV1.ConcurrentChange, PurchaseIncomingInvoice.StateConflict, PurchaseIncomingInvoice.MakerChecker,
            PurchaseIncomingInvoice.MatchRequired, PurchaseIncomingInvoice.VarianceApprovalRequired, PurchaseIncomingInvoice.SourceMismatch,
            PurchaseIncomingInvoice.PieceQuantityInvalid)
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    private static void InvoiceWorkspaceRead(RouteHandlerBuilder endpoint) => endpoint
        .ProducesErpBadRequest("validation_failed").AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
        .ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
