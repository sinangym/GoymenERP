using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;

namespace GoymenERP.Api.Security;

public sealed class ErpPermissionAuthorizationHandler(
    IErpAuthorizationService authorizationService,
    ICompanyContext companyContext)
    : AuthorizationHandler<ErpPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ErpPermissionRequirement requirement)
    {
        if (!companyContext.IsResolved)
        {
            return;
        }

        if (await authorizationService.HasPermissionAsync(
                requirement.Permission,
                companyContext.CompanyId))
        {
            context.Succeed(requirement);
        }
    }
}
