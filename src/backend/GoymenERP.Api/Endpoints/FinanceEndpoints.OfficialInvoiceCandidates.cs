using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class FinanceEndpoints
{
    private static void MapOfficialInvoiceCandidates(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{OfficialSalesInvoicesPath}/currency-candidates", async (
            HttpContext http, IFinanceOfficialSalesInvoiceCandidatesV1 service, ICompanyContext company,
            ICurrentUser user, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http);
            return Read(await service.ReadCurrenciesAsync(Context(company, user, correlation), token));
        }).WithName("ListFinanceOfficialSalesInvoiceCurrenciesV1").WithTags("Finance").RequireAuthorization().RequireCompanyContext()
            .Produces<FinanceOfficialSalesInvoiceCurrenciesV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody().AllowsCodeLessErpBadRequestBody().ProducesErpServiceUnavailable();
    }
}
