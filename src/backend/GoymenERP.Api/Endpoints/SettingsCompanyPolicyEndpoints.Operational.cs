using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsCompanyPolicyEndpoints
{
    private static void MapOperationalPolicies(RouteGroupBuilder group)
    {
        group.MapGet("/manufacturing-scrap-cost", async (HttpContext context, ISettingsOperationalPolicyAdministrationV1 service,
            CancellationToken cancellationToken) => await ReadAsync(context, () => service.GetScrapCostAsync(cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsOperationalPolicyPermissionsV1.ScrapCostManage)
            .WithName("GetSettingsManufacturingScrapCostV1").Produces<ManufacturingScrapCostPolicyViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/manufacturing-scrap-cost/versions", async (RecordManufacturingScrapCostRequestV1 request,
            ISettingsOperationalPolicyAdministrationV1 service, ICurrentUser currentUser, ICompanyContext companyContext,
            ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
                await CommandAsync(context, currentUser, companyContext, correlation,
                    metadata => service.RecordScrapCostAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsOperationalPolicyPermissionsV1.ScrapCostManage)
            .WithName("RecordSettingsManufacturingScrapCostV1").WithIdempotencyContract(204, 409)
            .Accepts<RecordManufacturingScrapCostRequestV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CompanyPolicyProblemCodesV1.IdempotencyConflict, CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapGet("/notification-retention", async (HttpContext context, ISettingsOperationalPolicyAdministrationV1 service,
            CancellationToken cancellationToken) => await ReadAsync(context, () => service.GetNotificationRetentionAsync(cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage)
            .WithName("GetSettingsNotificationRetentionV1").Produces<NotificationRetentionPolicyViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/notification-retention/versions", async (RecordNotificationRetentionRequestV1 request,
            ISettingsOperationalPolicyAdministrationV1 service, ICurrentUser currentUser, ICompanyContext companyContext,
            ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
                await CommandAsync(context, currentUser, companyContext, correlation,
                    metadata => service.RecordNotificationRetentionAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage)
            .WithName("RecordSettingsNotificationRetentionV1").WithIdempotencyContract(204, 409)
            .Accepts<RecordNotificationRetentionRequestV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CompanyPolicyProblemCodesV1.IdempotencyConflict, CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
    }
}
