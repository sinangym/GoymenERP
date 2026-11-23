using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    private static void MapCreditMovementEndpoints(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/finance/credit-movements";
        endpoints.MapGet(path, async (Guid creditAccountId, IFinanceCreditMovementServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.ListAsync(Context(company, user, correlation), creditAccountId, token));
        }).WithName("ListFinanceCreditMovementsV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<CreditMovementPageV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
        CreditMovementProblems(endpoints.MapPost(path, async (IFinanceCreditMovementServiceV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<CreditMovementProposeV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            var result = await service.ProposeAsync(Context(company, user, correlation), new(key), body, token);
            return Command(http, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
        }).WithName("ProposeFinanceCreditMovementV1").Accepts<CreditMovementProposeV1>("application/json"));
        CreditMovementProblems(endpoints.MapPost(path + "/{movementId:guid}/decision", async (Guid movementId,
            IFinanceCreditMovementServiceV1 service, ICompanyContext company, ICurrentUser user, ICorrelationContext correlation,
            HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<CreditMovementDecisionBodyV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            var result = await service.DecideAsync(Context(company, user, correlation), new(key), new(movementId, body.ExpectedVersion, body.Approve, body.Reason), token);
            return Command(http, result, result.Outcome, result.IsReplay, result.OriginalCorrelationId);
        }).WithName("DecideFinanceCreditMovementV1").Accepts<CreditMovementDecisionBodyV1>("application/json"));
    }
    private static RouteHandlerBuilder CreditMovementProblems(RouteHandlerBuilder builder) => builder.WithTags("Finance")
        .RequireAuthorization().RequireCompanyContext()
        .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
        .Produces<CreditMovementResultV1>()
        .Produces<CreditMovementResultV1>(StatusCodes.Status422UnprocessableEntity)
        .Produces<CreditMovementResultV1>(StatusCodes.Status409Conflict)
        .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody()
        .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
}
