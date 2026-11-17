using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    private static void MapBankCreditLimitEndpoints(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/finance/credit-bank-limits";
        endpoints.MapGet(path, async (Guid creditAccountId, IFinanceCreditLimitServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.ReadAsync(Context(company, user, correlation), creditAccountId, token));
        }).WithName("ListFinanceBankCreditLimitsV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<CreditLimitWorkspaceV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        CreditLimitProblems(endpoints.MapPost(path, async (IFinanceCreditLimitServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<CreditLimitProposeV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            var result = await service.ProposeAsync(Context(company, user, correlation), new(key), body, token);
            return Command(http, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
        }).WithName("ProposeFinanceBankCreditLimitV1").Accepts<CreditLimitProposeV1>("application/json"));
        CreditLimitProblems(endpoints.MapPost(path + "/{limitId:guid}/decision", async (Guid limitId,
            IFinanceCreditLimitServiceV1 service, ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
            HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<CreditLimitDecisionBodyV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            var result = await service.DecideAsync(Context(company, user, correlation), new(key), new(limitId, body.ExpectedVersion, body.Approve, body.Reason), token);
            return Command(http, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
        }).WithName("DecideFinanceBankCreditLimitV1").Accepts<CreditLimitDecisionBodyV1>("application/json"));
    }
    private static RouteHandlerBuilder CreditLimitProblems(RouteHandlerBuilder builder) => builder.WithTags("Finance")
        .RequireAuthorization().RequireCompanyContext().Produces<CreditLimitResultV1>()
        .Produces<CreditLimitResultV1>(StatusCodes.Status422UnprocessableEntity)
        .Produces<CreditLimitResultV1>(StatusCodes.Status409Conflict)
        .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
}
