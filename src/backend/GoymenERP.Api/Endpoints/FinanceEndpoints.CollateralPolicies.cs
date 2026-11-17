using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    private static IEndpointRouteBuilder MapFinanceCollateralPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(CollateralsPath + "/bank-candidates", async (HttpContext http, IFinanceCollateralBankCandidatesV1 reader,
            ICompanyContext company, ICurrentUser user, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            if (http.Request.Query.Count != 0) return Results.Problem(statusCode: 400);
            return Read(await reader.BankCandidatesAsync(Context(company, user, correlation), token));
        }).WithName("GetFinancePartyCollateralBankCandidatesV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<PartyCollateralBankCandidates>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpServiceUnavailable();
        return endpoints;
    }
}
