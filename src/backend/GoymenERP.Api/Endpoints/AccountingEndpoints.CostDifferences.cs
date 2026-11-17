using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    private static void MapManufacturingCostDifferences(RouteGroupBuilder group)
    {
        group.MapGet("/credit-movement-proposals", async (CreditMovementProposalServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.ListAsync(token), null);
        }).RequireCompanyContext().RequireAuthorization(GoymenERP.BuildingBlocks.Security.ErpPermissions.AccountingJournalsRead)
            .WithName("ListAccountingCreditMovementProposalsV1").Produces<CreditMovementProposalListV1>().WithReadProblems(notFound: false);

        // Read authorization is propose OR approve, measured by the owner service before and after its read.
        group.MapGet("/manufacturing-cost-differences", async (ManufacturingCostDifferenceServiceV1 service,
            HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.ListAsync(token), null);
        }).RequireCompanyContext().WithName("ListAccountingManufacturingCostDifferencesV1")
            .Produces<ManufacturingCostDifferenceListV1>(StatusCodes.Status200OK).WithReadProblems(notFound: false);

        CostDifferenceProblems(group.MapPost("/manufacturing-cost-differences/preparation", async (
            PrepareManufacturingCostDifferenceV1 request, ManufacturingCostDifferenceServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.PrepareAsync(request, key, token)))
            .RequireCompanyContext().RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Propose)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("PrepareAccountingManufacturingCostDifferenceV1")
            .Accepts<PrepareManufacturingCostDifferenceV1>("application/json")
            .Produces<ManufacturingCostDifferenceViewV1>(StatusCodes.Status200OK));

        CostDifferenceProblems(group.MapPost("/manufacturing-cost-differences/{proposalId:guid}/approval", async (
            Guid proposalId, ApproveManufacturingCostDifferenceV1 request, ManufacturingCostDifferenceServiceV1 service,
            HttpContext http, CancellationToken token) => await WriteAsync(http, key => service.ApproveAsync(proposalId, request.ExpectedVersion, key, token)))
            .RequireCompanyContext().RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Approve)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ApproveAccountingManufacturingCostDifferenceV1")
            .Accepts<ApproveManufacturingCostDifferenceV1>("application/json")
            .Produces<ManufacturingCostDifferenceViewV1>(StatusCodes.Status200OK));
    }

    private static RouteHandlerBuilder CostDifferenceProblems(RouteHandlerBuilder builder) => builder
        .ProducesErpBadRequest(AccountingApiCodes.RequestInvalid).AllowsCodeLessErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpConflict(AccountingApiCodes.IdempotencyConflict, AccountingApiCodes.VersionConflict,
            AccountingStoreCodes.ConcurrentChange, "cost_difference_source_changed", "cost_difference_source_conflict", "cost_difference_posting_conflict")
        .ProducesErpUnprocessableEntity("cost_difference_not_preparable", "cost_difference_preparation_invalid",
            "cost_difference_open_period_required", "cost_difference_account_plan_missing", "cost_difference_account_not_in_plan",
            "cost_difference_amount_unaccepted", "cost_difference_posting_refused")
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
