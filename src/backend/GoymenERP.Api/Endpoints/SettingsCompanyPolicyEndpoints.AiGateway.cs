using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsCompanyPolicyEndpoints
{
    /// <summary>
    /// WP-A1 (AI-D24, AI-D30): the company's AI gateway policy — task kind × data class model table, monthly usage ceiling,
    /// per-request reservation, feature switches — read with the current month's committed usage, and recorded as a new
    /// version on <see cref="SettingsAiGatewayPermissionsV1.Manage"/> (registered, granted to no role).
    /// </summary>
    private static void MapAiGatewayPolicy(RouteGroupBuilder group)
    {
        group.MapGet("/ai-gateway", async (HttpContext context, ISettingsAiGatewayAdministrationV1 service,
            CancellationToken cancellationToken) => await ReadAsync(context, () => service.GetAiGatewayPolicyAsync(cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsAiGatewayPermissionsV1.Manage)
            .WithName("GetSettingsAiGatewayPolicyV1").Produces<AiGatewayPolicyViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/ai-gateway/versions", async (RecordAiGatewayPolicyRequestV1 request,
            ISettingsAiGatewayAdministrationV1 service, ICurrentUser currentUser, ICompanyContext companyContext,
            ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
                await CommandAsync(context, currentUser, companyContext, correlation,
                    metadata => service.RecordAiGatewayPolicyAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsAiGatewayPermissionsV1.Manage)
            .WithName("RecordSettingsAiGatewayPolicyV1").WithIdempotencyContract(204, 409)
            .Accepts<RecordAiGatewayPolicyRequestV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CompanyPolicyProblemCodesV1.IdempotencyConflict, CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        // WP-A5 (AI-20): the current month's usage per feature from the ledger and, once WP-C9 registers a source, the
        // acceptance counts. Same permission and fence as the policy read; counts only.
        group.MapGet("/ai-gateway/usage", async (HttpContext context, ISettingsAiGatewayAdministrationV1 service,
            IServiceProvider services, ICompanyContext companyContext, CancellationToken cancellationToken) =>
                await ReadAsync(context, async () =>
                {
                    var usage = await service.GetAiGatewayUsageAsync(cancellationToken);
                    var source = services.GetService<IAiAcceptanceCountSourceV1>();
                    if (source is null) return AiGatewayUsageResponseV1.From(usage, null);
                    if (companyContext.CompanyId != usage.CompanyId) throw new UnauthorizedAccessException();
                    IReadOnlyList<AiFeatureAcceptanceCountV1> counts;
                    try
                    {
                        counts = await source.GetMonthAsync(usage.CompanyId, usage.BudgetMonth, cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
                    }

                    return AiGatewayUsageResponseV1.From(usage, counts);
                }))
            .RequireCompanyContext().RequireAuthorization(SettingsAiGatewayPermissionsV1.Manage)
            .WithName("GetSettingsAiGatewayUsageV1").Produces<AiGatewayUsageResponseV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
    }
}
