using GoymenERP.Api.Multitenancy;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    public static IEndpointRouteBuilder MapAccountingPostingMappingWorkspaceV1(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/accounting/posting-account-mappings/workspace", async (
            AccountingPostingAccountMappingServiceV1 service, HttpContext http, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response.Headers);
            return ToHttpResult(http, await service.WorkspaceAsync(token), null);
        }).RequireCompanyContext().RequireAuthorization(AccountingPostingAccountMappingPermissionNamesV1.Read)
            .WithName("GetAccountingPostingAccountMappingWorkspaceV1")
            .Produces<AccountingPostingAccountMappingWorkspaceV1>().WithReadProblems(notFound: false);
        return endpoints;
    }
}
