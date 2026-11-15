using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

/// <summary>
/// Answers whether the AI panel may ask in the current server company context. It is a snapshot for the UI only:
/// <see cref="AiAskService"/> measures authority again, fresh, on every ask. Nothing here calls the provider or a
/// tool, so no security event is written.
/// </summary>
public sealed class AiCapabilityService(
    IOptions<AiRuntimeOptions> options,
    IHostEnvironment environment,
    IAiModelProviderV1 provider,
    AiToolCatalog catalog,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorization)
{
    public async Task<AiCapabilitiesV1> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = AiRuntimeStateEvaluator.Evaluate(options.Value, environment, provider);
        if (!state.IsReady)
        {
            return new AiCapabilitiesV1(
                state.Availability == AiRuntimeAvailability.Disabled
                    ? AiAvailabilityValuesV1.Disabled
                    : AiAvailabilityValuesV1.Unavailable,
                PanelAvailable: false,
                Tools: []);
        }

        if (!currentUser.IsAuthenticated
            || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return new AiCapabilitiesV1(AiAvailabilityValuesV1.Ready, PanelAvailable: false, Tools: []);
        }

        var companyId = companyContext.CompanyId;
        var panelScope = await authorization.GetPermissionScopeAsync(ErpPermissions.AiPanelUse, companyId, cancellationToken);
        if (panelScope is not { IsGranted: true, IsCompanyWide: true })
        {
            return new AiCapabilitiesV1(AiAvailabilityValuesV1.Ready, PanelAvailable: false, Tools: []);
        }

        var limits = state.Limits!;
        var tools = new List<AiToolCapabilityV1>();
        foreach (var descriptor in catalog.Descriptors.Where(descriptor => descriptor.DataClass <= limits.MaxDataClass))
        {
            var granted = true;
            foreach (var permission in descriptor.RequiredPermissions)
            {
                var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
                if (scope is not { IsGranted: true, IsCompanyWide: true })
                {
                    granted = false;
                    break;
                }
            }

            if (granted)
            {
                tools.Add(new AiToolCapabilityV1(
                    descriptor.ToolId,
                    descriptor.Version,
                    descriptor.OwnerModule,
                    descriptor.DataClass.ToString()));
            }
        }

        return new AiCapabilitiesV1(AiAvailabilityValuesV1.Ready, PanelAvailable: true, Tools: tools);
    }
}
