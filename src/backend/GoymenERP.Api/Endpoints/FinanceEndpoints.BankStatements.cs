using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP12 (FNS-D13/D52): bank statement import routes. 401/403/404/503 are code-less; 409/422 carry the command result with
/// its code (and the file issues for <see cref="FinanceBankStatementCodesV1.FileInvalid"/>).
/// </summary>
public static partial class FinanceEndpoints
{
    public const string BankStatementImportsPath = "/api/v1/finance/bank-statement-imports";

    public static IEndpointRouteBuilder MapFinanceBankStatementImportsV1(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BankStatementImportsPath, async (Guid? bankAccountId, IFinanceBankStatementImportServiceV1 service,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            var result = await service.ListAuthorizedAsync(Context(company, user, correlation), bankAccountId, token);
            return result.Outcome switch
            {
                ReadOutcome.Found => Results.Ok(result.List),
                ReadOutcome.Denied => Results.Problem(statusCode: 403),
                _ => Results.Problem(statusCode: 503),
            };
        }).WithName("ListFinanceBankStatementImportsV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinanceBankStatementImportListV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpServiceUnavailable();

        endpoints.MapGet(BankStatementImportsPath + "/{importId:guid}", async (Guid importId,
            IFinanceBankStatementImportServiceV1 service, ICompanyContext company, ICurrentUser user,
            ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            var result = await service.GetAuthorizedAsync(Context(company, user, correlation), importId, token);
            return result.Outcome switch
            {
                ReadOutcome.Found => Results.Ok(result.Detail),
                ReadOutcome.NotFound => Results.Problem(statusCode: 404),
                ReadOutcome.Denied => Results.Problem(statusCode: 403),
                _ => Results.Problem(statusCode: 503),
            };
        }).WithName("GetFinanceBankStatementImportV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinanceBankStatementImportDetailV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapPost(BankStatementImportsPath, async (IFinanceBankStatementImportServiceV1 service,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<ReserveFinanceBankStatementImportV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            return BankStatementCommand(http,
                await service.ReserveAsync(Context(company, user, correlation), new(key), body, token));
        }).WithName("ReserveFinanceBankStatementImportV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Accepts<ReserveFinanceBankStatementImportV1>("application/json").Produces<FinanceBankStatementCommandResultV1>()
            .Produces<FinanceBankStatementCommandResultV1>(409).Produces<FinanceBankStatementCommandResultV1>(422)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType().ProducesErpServiceUnavailable();

        endpoints.MapPost(BankStatementImportsPath + "/{importId:guid}/parse", async (Guid importId,
            IFinanceBankStatementImportServiceV1 service, ICompanyContext company, ICurrentUser user,
            ICorrelationContext correlation, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (!TryReadIdempotencyKey(http.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (await ReadBodyAsync<ParseFinanceBankStatementImportV1>(http, token) is not { } body)
                return http.Request.HasJsonContentType() ? Results.Problem(statusCode: 400) : Results.StatusCode(415);
            return BankStatementCommand(http,
                await service.ParseAsync(Context(company, user, correlation), new(key), importId, body, token));
        }).WithName("ParseFinanceBankStatementImportV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Accepts<ParseFinanceBankStatementImportV1>("application/json").Produces<FinanceBankStatementCommandResultV1>()
            .Produces<FinanceBankStatementCommandResultV1>(409).Produces<FinanceBankStatementCommandResultV1>(422)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();
        return endpoints;
    }

    private static IResult BankStatementCommand(HttpContext http, FinanceBankStatementCommandResultV1 result)
    {
        if (result.Replayed) http.Response.Headers["Idempotency-Replayed"] = "true";
        return result.Outcome switch
        {
            CommandOutcome.Succeeded => Results.Ok(result),
            CommandOutcome.Denied => Results.Problem(statusCode: 403),
            CommandOutcome.ValidationFailed when result.Code == FinanceBankStatementCodesV1.ImportNotFound =>
                Results.Problem(statusCode: 404),
            CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => Results.Json(result, statusCode: 409),
            CommandOutcome.ValidationFailed => Results.Json(result, statusCode: 422),
            _ => Results.Problem(statusCode: 503),
        };
    }
}
