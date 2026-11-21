using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsCompanyPolicyEndpoints
{
    private static void MapPurchasingSubcontractTaxDefault(RouteGroupBuilder group)
    {
        foreach (var (path, side, permission, operation) in new[] {
            ("sales", SubcontractTaxSideV1.Sales, ErpPermissions.SalesOrdersApprove, "GetSettingsSalesSubcontractTaxOptionsV1"),
            ("purchasing", SubcontractTaxSideV1.Purchasing, ErpPermissions.PurchasingOrdersApprove, "GetSettingsPurchasingSubcontractTaxOptionsV1") })
            group.MapGet($"/{path}-subcontract-tax-default/options", async (HttpContext context,
                ISettingsCompanyPolicyAdministrationV1 service, CancellationToken token) =>
                await ReadAsync(context, () => service.GetSubcontractTaxOptionsAsync(side, token)))
                .RequireCompanyContext().RequireAuthorization(permission).WithName(operation).Produces<SubcontractTaxOptionsV1>()
                .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
                .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapGet("/purchasing-subcontract-tax-default", async (HttpContext context,
            ISettingsCompanyPolicyAdministrationV1 service, CancellationToken token) =>
            await ReadAsync(context, () => service.GetPurchasingSubcontractTaxDefaultAsync(token)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.PurchasingOrdersApprove)
            .WithName("GetSettingsPurchasingSubcontractTaxDefaultV1").Produces<SubcontractTaxDefaultViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/purchasing-subcontract-tax-default/versions", async (RecordSubcontractTaxDefaultRequestV1 request,
            ISettingsCompanyPolicyAdministrationV1 service, ICurrentUser user, ICompanyContext company,
            ICorrelationContext correlation, HttpContext context, CancellationToken token) =>
            await CommandAsync(context, user, company, correlation, metadata => service.RecordPurchasingSubcontractTaxDefaultAsync(request, metadata, token)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.PurchasingOrdersApprove)
            .WithName("RecordSettingsPurchasingSubcontractTaxDefaultV1")
            .WithIdempotencyContract(204, 409, 422).Accepts<RecordSubcontractTaxDefaultRequestV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CompanyPolicyProblemCodesV1.IdempotencyConflict, CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(CompanyPolicyProblemCodesV1.ExemptionCodeUnknown, CompanyPolicyProblemCodesV1.WithholdingCodeUnknown, CompanyPolicyProblemCodesV1.RateUnknown)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
    }
}
