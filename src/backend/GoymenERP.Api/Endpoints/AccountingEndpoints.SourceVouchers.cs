using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    /// <summary>Mounted by the composition root once; the module registers no role or grant.</summary>
    public static void MapAccountingSourceVouchersV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/accounting").RequireAuthorization();
        group.MapGet("/source-vouchers", async (AccountingSourceVoucherServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.ListAsync(token), null);
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.AccountingJournalsRead)
            .WithName("ListAccountingSourceVouchersV1").Produces<AccountingSourceVoucherListV1>().WithReadProblems(notFound: false);
        group.MapPost("/source-vouchers/{id:guid}/approval", async (Guid id, ApproveAccountingSourceVoucherV1 request,
            AccountingSourceVoucherServiceV1 service, HttpContext http, CancellationToken token) =>
            await WriteAsync(http, key => service.ApproveAsync(id, request.ExpectedVersion, key, token)))
            .RequireCompanyContext().RequireAuthorization(NormalJournalPermissionNamesV1.DraftApprove)
            .WithName("ApproveAccountingSourceVoucherV1").Accepts<ApproveAccountingSourceVoucherV1>("application/json")
            .Produces<AccountingSourceVoucherViewV1>().WithIdempotencyContract(200, 404, 409)
            .ProducesErpBadRequest(AccountingApiCodes.RequestInvalid).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(AccountingApiCodes.VersionConflict, AccountingApiCodes.IdempotencyConflict,
                AccountingStoreCodes.ConcurrentChange, "source_voucher_not_approvable", "source_voucher_posting_conflict")
            .ProducesErpUnprocessableEntity("source_voucher_period_waiting", "source_voucher_account_plan_missing",
                "source_voucher_account_not_in_plan", "source_voucher_amount_unaccepted", "source_voucher_posting_refused")
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
        group.MapPost("/source-vouchers/{id:guid}/cancellation-reversal-proposals", async (Guid id,
            ProposeAccountingSourceVoucherReversalV1 request, AccountingSourceVoucherServiceV1 service,
            HttpContext http, CancellationToken token) =>
            await WriteAsync(http, key => service.ProposeCancellationReversalAsync(id, request, key, token)))
            .RequireCompanyContext().RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Propose)
            .WithName("ProposeAccountingSourceVoucherCancellationReversalV1")
            .Accepts<ProposeAccountingSourceVoucherReversalV1>("application/json")
            .Produces<AccountingSourceVoucherViewV1>().WithIdempotencyContract(200, 404, 409)
            .ProducesErpBadRequest(AccountingApiCodes.RequestInvalid).AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpConflict(AccountingApiCodes.VersionConflict, AccountingApiCodes.IdempotencyConflict,
                AccountingStoreCodes.ConcurrentChange, "source_voucher_reversal_not_proposable",
                "source_voucher_source_conflict", AccountingJournalReversalStoreCodesV1.ContentConflict)
            .ProducesErpUnprocessableEntity("source_voucher_reversal_period_not_open",
                AccountingJournalReversalStoreCodesV1.SourceJournalNotFound, AccountingJournalReversalStoreCodesV1.PeriodNotFound,
                "source_voucher_posting_refused")
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}
