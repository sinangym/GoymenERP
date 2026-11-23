using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsManufacturingProfileEndpoints
{
    private static void MapBaselineTransfers(IEndpointRouteBuilder endpoints)
    {
        Transfer(endpoints.MapPost(ParameterProfilesPath + "/starting-values-transfer", async (
            HttpContext http, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation,
            ManufacturingProfileBaselineTransferRequestV1 request, ISettingsManufacturingParameterProfileAdministrationV1 service,
            CancellationToken token) => await CommandAsync(http, user, company, correlation,
                metadata => service.TransferStartingValuesAsync(request, metadata, token))),
            ManufacturingParameterProfileActionsV1.Publish, "TransferSettingsManufacturingParameterStartingValuesV1");
        Transfer(endpoints.MapPost(ToleranceProfilesPath + "/starting-values-transfer", async (
            HttpContext http, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation,
            ManufacturingProfileBaselineTransferRequestV1 request, ISettingsManufacturingToleranceProfileAdministrationV1 service,
            CancellationToken token) => await CommandAsync(http, user, company, correlation,
                metadata => service.TransferStartingValuesAsync(request, metadata, token))),
            ManufacturingToleranceProfileActionsV1.Publish, "TransferSettingsManufacturingToleranceStartingValuesV1");
    }

    private static RouteHandlerBuilder Transfer(RouteHandlerBuilder route, string permission, string name) => route
        .RequireCompanyContext().RequireAuthorization(permission).WithTags("Settings").WithName(name)
        .Accepts<ManufacturingProfileBaselineTransferRequestV1>("application/json")
        .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict)
        .Produces<ManufacturingProfileCommandReceiptV1>(StatusCodes.Status200OK)
        .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody()
        .ProducesErpCodeLessNotFound().ProducesErpUnsupportedMediaType()
        .ProducesErpConflict(ManufacturingProfileProblemCodesV1.IdempotencyConflict, ManufacturingProfileProblemCodesV1.VersionConflict)
        .ProducesErpServiceUnavailable(ManufacturingProfileProblemCodesV1.ProviderUnavailable)
        .ProducesErpAuthenticatedProblems();
}
