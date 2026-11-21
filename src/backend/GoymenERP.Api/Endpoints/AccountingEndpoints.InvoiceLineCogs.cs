using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    public static IEndpointRouteBuilder MapAccountingInvoiceLineCogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/accounting/invoice-line-cogs").RequireAuthorization().WithTags("Accounting");
        group.MapGet("/source", async (InvoiceLineCogsCorrectionServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return http.Request.Query.Count != 1 || !http.Request.Query.TryGetValue("officialInvoiceNumber", out var numbers)
                || numbers.Count != 1 || numbers[0] is not { Length: > 0 and <= 64 } number || number != number.Trim()
                ? Problem(400, AccountingApiCodes.RequestInvalid, "officialInvoiceNumber")
                : ToHttpResult(http, await service.FindSourceAsync(number, token), null);
        }).RequireCompanyContext().WithName("FindAccountingInvoiceLineCogsSourceV1")
            .Produces<FinalizedInvoiceCogsSelectionV1>().WithReadProblems(notFound: true)
            .ProducesErpUnprocessableEntity(InvoiceLineCogsCorrectionCodesV1.RequestInvalid);
        group.MapGet("/options", async (InvoiceLineCogsCorrectionServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return http.Request.Query.Count != 0 ? Problem(400, AccountingApiCodes.RequestInvalid, "query")
                : ToHttpResult(http, await service.OptionsAsync(token), null);
        }).RequireCompanyContext().WithName("GetAccountingInvoiceLineCogsOptionsV1")
            .Produces<InvoiceLineCogsOptionsV1>().WithReadProblems(notFound: false);

        group.MapGet("/{invoiceId:guid}/lines/{invoiceLineId:guid}", async (Guid invoiceId, Guid invoiceLineId,
            InvoiceLineCogsCorrectionServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return http.Request.Query.Count != 0 ? Problem(400, AccountingApiCodes.RequestInvalid, "query")
                : ToHttpResult(http, await service.ReadAsync(invoiceId, invoiceLineId, token), null);
        }).RequireCompanyContext().WithName("GetAccountingInvoiceLineCogsHistoryV1")
            .Produces<InvoiceLineCogsHistoryV1>().WithReadProblems(notFound: true)
            .ProducesErpConflict(InvoiceLineCogsCorrectionCodesV1.SourceChanged);

        InvoiceLineProblems(group.MapPost("/{invoiceId:guid}/lines/{invoiceLineId:guid}/preparation", async (
            Guid invoiceId, Guid invoiceLineId, PrepareInvoiceLineCogsCorrectionV1 request,
            InvoiceLineCogsCorrectionServiceV1 service, HttpContext http, CancellationToken token) =>
            http.Request.Query.Count != 0 ? Problem(400, AccountingApiCodes.RequestInvalid, "query")
                : await WriteAsync(http, key => service.PrepareAsync(invoiceId, invoiceLineId, request, key, token)))
            .RequireCompanyContext().RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Propose)
            .WithName("PrepareAccountingInvoiceLineCogsCorrectionV1")
            .WithIdempotencyContract(200, 404, 409).Accepts<PrepareInvoiceLineCogsCorrectionV1>("application/json")
            .Produces<InvoiceLineCogsCorrectionViewV1>());

        InvoiceLineProblems(group.MapPost("/{invoiceId:guid}/lines/{invoiceLineId:guid}/proposals/{proposalId:guid}/approval", async (
            Guid invoiceId, Guid invoiceLineId, Guid proposalId, ApproveInvoiceLineCogsCorrectionV1 request,
            InvoiceLineCogsCorrectionServiceV1 service, HttpContext http, CancellationToken token) =>
            http.Request.Query.Count != 0 ? Problem(400, AccountingApiCodes.RequestInvalid, "query")
                : await WriteAsync(http, key => service.ApproveAsync(invoiceId, invoiceLineId, proposalId, request, key, token)))
            .RequireCompanyContext().RequireAuthorization(AccountingJournalReversalPermissionNamesV1.Approve)
            .WithName("ApproveAccountingInvoiceLineCogsCorrectionV1")
            .WithIdempotencyContract(200, 404, 409).Accepts<ApproveInvoiceLineCogsCorrectionV1>("application/json")
            .Produces<InvoiceLineCogsCorrectionViewV1>());
        return endpoints;
    }

    private static RouteHandlerBuilder InvoiceLineProblems(RouteHandlerBuilder builder) => builder
        .ProducesErpBadRequest(AccountingApiCodes.RequestInvalid).AllowsCodeLessErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
        .ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable()
        .ProducesErpConflict(AccountingApiCodes.IdempotencyConflict, AccountingApiCodes.VersionConflict,
            AccountingStoreCodes.ConcurrentChange, InvoiceLineCogsCorrectionCodesV1.SourceChanged,
            InvoiceLineCogsCorrectionCodesV1.VersionConflict, InvoiceLineCogsCorrectionCodesV1.NotPrepared)
        .ProducesErpUnprocessableEntity(InvoiceLineCogsCorrectionCodesV1.RequestInvalid,
            InvoiceLineCogsCorrectionCodesV1.OpenPeriodRequired, InvoiceLineCogsCorrectionCodesV1.AccountNotInActivePlan,
            InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
}
