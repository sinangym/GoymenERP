using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    private static void MapFinanceJournalSources(RouteGroupBuilder group)
    {
        group.MapGet("/finance-journal-sources", async (FinanceJournalSourceServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.ListAsync(token), null);
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.AccountingJournalsRead)
            .WithName("ListAccountingFinanceJournalSourcesV1").Produces<FinanceJournalSourceListV1>().WithReadProblems(notFound: false);
    }
}
