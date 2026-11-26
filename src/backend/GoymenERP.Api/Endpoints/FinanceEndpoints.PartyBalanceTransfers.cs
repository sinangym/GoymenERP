using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinancePartyBalanceTransfersV1(this IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/finance/party-balance-transfers";
        endpoints.MapGet(path + "/preview", async (Guid predecessorPartyId, Guid predecessorAccountId,
            Guid successorPartyId, Guid successorAccountId, IFinancePartyBalanceTransferServiceV1 service,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            var result = await service.PreviewAuthorizedAsync(Context(company, user, correlation),
                new(predecessorPartyId, predecessorAccountId, successorPartyId, successorAccountId), token);
            return BalanceTransferRead(result);
        }).WithName("PreviewFinancePartyBalanceTransferV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinancePartyBalanceTransferReadResultV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        endpoints.MapGet(path + "/{transferId:guid}", async (Guid transferId, IFinancePartyBalanceTransferServiceV1 service,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            return BalanceTransferRead(await service.GetAuthorizedAsync(Context(company, user, correlation), transferId, token));
        }).WithName("GetFinancePartyBalanceTransferV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinancePartyBalanceTransferReadResultV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        endpoints.MapPost(path, async (IFinancePartyBalanceTransferServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<RecordFinancePartyBalanceTransferV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            var result = await service.RecordAsync(Context(company, user, correlation), new(key), body, token);
            if (result.Replayed)
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                if (!string.IsNullOrWhiteSpace(result.OriginalCorrelationId))
                    http.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return result.Outcome switch
            {
                CommandOutcome.Succeeded => Results.Ok(result),
                CommandOutcome.Denied => Results.Problem(statusCode: 403),
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => Results.Json(result, statusCode: 409),
                CommandOutcome.ValidationFailed => Results.Json(result, statusCode: 422),
                _ => Results.Problem(statusCode: 503),
            };
        }).WithName("RecordFinancePartyBalanceTransferV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<RecordFinancePartyBalanceTransferV1>("application/json").Produces<FinancePartyBalanceTransferCommandResultV1>()
            .Produces<FinancePartyBalanceTransferCommandResultV1>(409).Produces<FinancePartyBalanceTransferCommandResultV1>(422)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable();
        endpoints.MapGet(PaymentAllocationsPath + "/candidates/{paymentId:guid}", async (Guid paymentId, int? pageIndex,
            IFinancePaymentAllocationCandidatesV1 service, ICompanyContext company, ICurrentUser user,
            ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.ReadAsync(Context(company, user, correlation), paymentId, pageIndex ?? 0, token));
        }).WithName("GetFinancePaymentAllocationCandidatesV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinancePaymentAllocationCandidatesViewV1>().ProducesErpAuthenticatedProblems().ProducesErpCodeLessNotFound()
            .ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpServiceUnavailable();
        return endpoints;
    }
    private static IResult BalanceTransferRead(FinancePartyBalanceTransferReadResultV1 result) => result.Outcome switch
    {
        ReadOutcome.Found => Results.Ok(result), ReadOutcome.NotFound => Results.Problem(statusCode: 404),
        ReadOutcome.Denied => Results.Problem(statusCode: 403), _ => Results.Problem(statusCode: 503),
    };
}
