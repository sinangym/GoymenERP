using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class AccountingEndpoints
{
    private static void MapPeriodLifecycle(RouteGroupBuilder group)
    {
        group.MapGet("/periods/{periodId:guid}/lifecycle", async (Guid periodId, AccountingApiService service,
            HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response.Headers);
            return ToHttpResult(httpContext, await service.GetPeriodLifecycleAsync(periodId, cancellationToken), null);
        }).RequireCompanyContext().RequireAuthorization(AccountingPermissionManifestV1.PeriodsRead)
            .WithName("GetAccountingPeriodLifecycleV1").Produces<AccountingPeriodLifecycleViewV1>(StatusCodes.Status200OK)
            .WithReadProblems(notFound: true);

        group.MapPost("/periods/{periodId:guid}/reopening-proposals", async (Guid periodId,
            ProposeAccountingPeriodReopeningV1 request, AccountingApiService service, HttpContext httpContext,
            CancellationToken cancellationToken) => await WriteAsync(httpContext,
                key => service.ProposePeriodReopeningAsync(periodId, request, key, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(AccountingPermissionManifestV1.PeriodsPropose)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ProposeAccountingPeriodReopeningV1").Accepts<ProposeAccountingPeriodReopeningV1>("application/json")
            .Produces<AccountingPeriodReopeningViewV1>(StatusCodes.Status200OK).WithWriteProblems(notFound: true);

        group.MapPost("/periods/{periodId:guid}/reopening-proposals/{proposalId:guid}/approval", async (Guid periodId,
            Guid proposalId, ApproveAccountingPeriodReopeningV1 request, AccountingApiService service, HttpContext httpContext,
            CancellationToken cancellationToken) => await WriteAsync(httpContext,
                key => service.ApprovePeriodReopeningAsync(periodId, proposalId, request, key, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(AccountingPermissionManifestV1.PeriodsApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict)
            .WithName("ApproveAccountingPeriodReopeningV1").Accepts<ApproveAccountingPeriodReopeningV1>("application/json")
            .Produces<AccountingPeriodReopeningViewV1>(StatusCodes.Status200OK).WithWriteProblems(notFound: true);
    }
}
