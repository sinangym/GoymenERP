using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceCashExchangeBookValuesV1(this IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/finance/cash-exchanges/book-values";
        endpoints.MapGet(path, async (Guid? afterId, ICashExchangeBookValueServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken ct) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.ListAsync(Context(company, user, correlation), afterId, ct));
        }).WithName("ListFinanceCashExchangeBookValueCandidatesV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<CashExchangeBookValueCandidatePageV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        endpoints.MapGet(path + "/{transferId:guid}", async (Guid transferId, ICashExchangeBookValueServiceV1 service,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken ct) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.GetAsync(Context(company, user, correlation), transferId, ct));
        }).WithName("GetFinanceCashExchangeBookValueWorkspaceV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<CashExchangeBookValueWorkspaceV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        endpoints.MapPost(path, async (ICashExchangeBookValueServiceV1 service, ICompanyContext company, ICurrentUser user,
            ICorrelationContext correlation, HttpContext http, CancellationToken ct) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<ProposeCashExchangeBookValueV1>(http, ct) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            return BookValueResult(http, await service.ProposeAsync(Context(company, user, correlation), new(key), body, ct));
        }).WithName("ProposeFinanceCashExchangeBookValueV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Accepts<ProposeCashExchangeBookValueV1>("application/json").Produces<CashExchangeBookValueCommandResultV1>()
            .Produces<CashExchangeBookValueCommandResultV1>(409).Produces<CashExchangeBookValueCommandResultV1>(422)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable();
        endpoints.MapPost(path + "/decision", async (ICashExchangeBookValueServiceV1 service, ICompanyContext company, ICurrentUser user,
            ICorrelationContext correlation, HttpContext http, CancellationToken ct) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<DecideCashExchangeBookValueV1>(http, ct) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            return BookValueResult(http, await service.DecideAsync(Context(company, user, correlation), new(key), body, ct));
        }).WithName("DecideFinanceCashExchangeBookValueV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Accepts<DecideCashExchangeBookValueV1>("application/json").Produces<CashExchangeBookValueCommandResultV1>()
            .Produces<CashExchangeBookValueCommandResultV1>(409).Produces<CashExchangeBookValueCommandResultV1>(422)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable();
        return endpoints;
    }

    private static IResult BookValueResult(HttpContext http, CashExchangeBookValueCommandResultV1 result) =>
        Command(http, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
}
