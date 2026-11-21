using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    /// <summary>Owner checks exact create/approve authority for each source kind; registration grants nothing.</summary>
    public static IEndpointRouteBuilder MapAccountingInventoryCostsV1(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/accounting/inventory-cost-proposals").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/{kind}", async (AccountingInventoryCostKindV1 kind, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.ListAsync(kind, token), null);
        }).WithName("ListAccountingInventoryCostProposalsV1").Produces<AccountingInventoryCostListV1>().WithReadProblems(notFound: false);
        group.MapGet("/{kind}/options", async (AccountingInventoryCostKindV1 kind, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.OptionsAsync(kind, token), null);
        }).WithName("GetAccountingInventoryCostProposalOptionsV1").Produces<InvoiceLineCogsOptionsV1>().WithReadProblems(notFound: false);
        InventoryCostProblems(group.MapPost("/{kind}/{proposalId:guid}/preparation", async (AccountingInventoryCostKindV1 kind,
            Guid proposalId, PrepareAccountingInventoryCostV1 request, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.PrepareAsync(proposalId, kind, request, key, token))))
            .WithName("PrepareAccountingInventoryCostProposalV1").Accepts<PrepareAccountingInventoryCostV1>("application/json");
        InventoryCostProblems(group.MapPost("/{kind}/{proposalId:guid}/approval", async (AccountingInventoryCostKindV1 kind,
            Guid proposalId, ApproveAccountingInventoryCostV1 request, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.ApproveAsync(proposalId, kind, request.ExpectedVersion, key, token))))
            .WithName("ApproveAccountingInventoryCostProposalV1").Accepts<ApproveAccountingInventoryCostV1>("application/json");
        InventoryCostProblems(group.MapPost("/{proposalId:guid}/cancellation-reversal-proposals", async (Guid proposalId,
            ProposeAccountingInventoryCostReversalV1 request, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.ProposeReversalAsync(proposalId, request, key, token))))
            .RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Propose)
            .WithName("ProposeAccountingInventoryCostCancellationReversalV1").Accepts<ProposeAccountingInventoryCostReversalV1>("application/json");
        InventoryCostProblems(group.MapPost("/{proposalId:guid}/component-cancellation-approval", async (Guid proposalId,
            ApproveInventoryCostCancellationV1 request, AccountingInventoryCostServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.ApproveCancellationAsync(proposalId, request, key, token))))
            .RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Approve)
            .WithName("ApproveAccountingInventoryCostComponentCancellationV1").Accepts<ApproveInventoryCostCancellationV1>("application/json");
        return app;
    }

    private static RouteHandlerBuilder InventoryCostProblems(RouteHandlerBuilder builder) => builder
        .Produces<AccountingInventoryCostProposalV1>()
        .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
        .ProducesErpBadRequest(AccountingApiCodes.RequestInvalid).AllowsCodeLessErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpConflict(AccountingApiCodes.IdempotencyConflict, AccountingApiCodes.VersionConflict, AccountingStoreCodes.ConcurrentChange,
            "inventory_cost_preparation_stale", "inventory_cost_source_conflict", "inventory_cost_posting_conflict")
        .ProducesErpUnprocessableEntity("inventory_cost_not_preparable", "inventory_cost_request_invalid", "inventory_cost_open_period_required",
            "inventory_cost_account_plan_missing", "inventory_cost_account_invalid", "inventory_cost_classification_missing", "inventory_cost_mapping_missing",
            "inventory_cost_amount_not_representable", "inventory_cost_amount_unaccepted", "inventory_cost_posting_refused",
            "inventory_cost_source_cancelled", "inventory_cost_reversal_not_proposable")
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
