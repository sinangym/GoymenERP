using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-029 S5/S6 (URUN_F029 spec §5.4(2); URN-D2…D5): the Sales quote policies (SAT-D25 loss reason list, SAT-D39 default
/// validity) and the e-Fatura scenario policy (SAT-D120 code list, SAT-D102 default scenario) as persistent, versioned
/// Settings records. Every route is in the server-resolved company (X-Company-Id) under the family's own name
/// (<see cref="SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1"/>; company-wide, not Critical, granted to no role
/// here), re-authorized by the service. Responses are private, no-store. The request handling (query refusal, Idempotency-Key,
/// replay headers, problem mapping) is <see cref="SettingsSalesPolicyEndpoints"/>' own.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/sales-policies/quote-policy</c> · <c>GET …/einvoice-scenarios</c> — both timelines of the
/// family (the reviewed catalog until the family's baseline is transferred, the stored versions afterwards).</item>
/// <item><c>POST …/quote-policy/baseline-transfer</c> · <c>POST …/einvoice-scenarios/baseline-transfer</c> (204) — the
/// one-time, explicit copy of both keys' reviewed catalog entries into the table.</item>
/// <item><c>POST …/quote-policy/loss-reasons/versions</c> · <c>…/quote-policy/default-validity/versions</c> ·
/// <c>…/einvoice-scenarios/code-lists/versions</c> · <c>…/einvoice-scenarios/defaults/versions</c> (204) — a new version
/// of one key from its business date (today or later).</item>
/// </list>
/// </summary>
public static class SettingsSalesQuoteAndEInvoicePolicyEndpoints
{
    public static IEndpointRouteBuilder MapSettingsSalesQuoteAndEInvoicePolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(SettingsSalesPolicyEndpoints.SalesPoliciesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        // SAT-D25 / SAT-D39: the company's quote policies.
        group.MapGet("/quote-policy", async (
                HttpContext httpContext,
                ISettingsSalesQuotePolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.ReadAsync(httpContext,
                () => service.GetSalesQuotePolicyAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage)
            .WithName("GetSettingsSalesQuotePolicyV1")
            .Produces<SalesQuotePolicyViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/quote-policy/baseline-transfer", async (
                TransferSalesPolicyBaselineRequestV1 request,
                ISettingsSalesQuotePolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.TransferSalesQuotePolicyBaselineAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage)
            .WithName("TransferSettingsSalesQuotePolicyBaselineV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<TransferSalesPolicyBaselineRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.AlreadyTransferred)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/quote-policy/loss-reasons/versions", async (
                RecordSalesQuoteLossReasonsRequestV1 request,
                ISettingsSalesQuotePolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordSalesQuoteLossReasonsAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage)
            .WithName("RecordSettingsSalesQuoteLossReasonsV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordSalesQuoteLossReasonsRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(
                SalesPolicyProblemCodesV1.EffectiveFromPast,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.LossReasonIdentityConflict)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/quote-policy/default-validity/versions", async (
                RecordSalesQuoteDefaultValidityRequestV1 request,
                ISettingsSalesQuotePolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordSalesQuoteDefaultValidityAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage)
            .WithName("RecordSettingsSalesQuoteDefaultValidityV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordSalesQuoteDefaultValidityRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(SalesPolicyProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D120 / SAT-D102: the company's e-Fatura scenario code list and default scenario.
        group.MapGet("/einvoice-scenarios", async (
                HttpContext httpContext,
                ISettingsEInvoiceScenarioPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.ReadAsync(httpContext,
                () => service.GetEInvoiceScenarioPolicyAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage)
            .WithName("GetSettingsEInvoiceScenarioPolicyV1")
            .Produces<EInvoiceScenarioPolicyViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/einvoice-scenarios/baseline-transfer", async (
                TransferSalesPolicyBaselineRequestV1 request,
                ISettingsEInvoiceScenarioPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.TransferEInvoiceScenarioBaselineAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage)
            .WithName("TransferSettingsEInvoiceScenarioBaselineV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<TransferSalesPolicyBaselineRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.AlreadyTransferred)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/einvoice-scenarios/code-lists/versions", async (
                RecordEInvoiceScenarioCodeListRequestV1 request,
                ISettingsEInvoiceScenarioPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordEInvoiceScenarioCodeListAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage)
            .WithName("RecordSettingsEInvoiceScenarioCodeListV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordEInvoiceScenarioCodeListRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(
                SalesPolicyProblemCodesV1.EffectiveFromPast,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/einvoice-scenarios/defaults/versions", async (
                RecordEInvoiceScenarioDefaultRequestV1 request,
                ISettingsEInvoiceScenarioPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await SettingsSalesPolicyEndpoints.CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordEInvoiceScenarioDefaultAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage)
            .WithName("RecordSettingsEInvoiceScenarioDefaultV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordEInvoiceScenarioDefaultRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(SettingsSalesPolicyEndpoints.CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(
                SalesPolicyProblemCodesV1.EffectiveFromPast,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }
}
