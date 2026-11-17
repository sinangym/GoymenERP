using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class IdentityEndpoints
{
    private static void MapUserManagement(RouteGroupBuilder group)
    {
        group.MapGet("/access/managed-users", async (HttpContext context, IIdentityUserManagementServiceV1 service, CancellationToken ct) =>
        {
            NoStore(context);
            return Results.Ok(await service.ReadUsersAsync(ct));
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.IdentityUsersManage)
            .WithName("GetManagedIdentityUsersV1").Produces<IReadOnlyList<IdentityManagedUserV1>>()
            .ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        var create = group.MapPost("/access/users", async (CreateLocalUserV1 request, HttpContext context,
            IIdentityUserManagementServiceV1 service, CancellationToken ct) =>
            await AccessCommandAsync(context, metadata => service.CreateUserAsync(request, metadata, ct)))
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.IdentityUsersManage)
            .WithName("CreateLocalIdentityUserV1").Accepts<CreateLocalUserV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(201);
        UserCommandContract(create);

        foreach (var action in new[] { IdentityUserManagementV1.ResetPassword, IdentityUserManagementV1.Unlock, IdentityUserManagementV1.Deactivate })
        {
            var name = action == IdentityUserManagementV1.ResetPassword ? "ResetLocalIdentityPasswordV1"
                : action == IdentityUserManagementV1.Unlock ? "UnlockLocalIdentityUserV1" : "DeactivateLocalIdentityUserV1";
            var endpoint = group.MapPost($"/access/users/{{userId:guid}}/{action}", async (Guid userId, ManageLocalUserV1 request,
                HttpContext context, IIdentityUserManagementServiceV1 service, CancellationToken ct) =>
                await AccessCommandAsync(context, metadata => service.ManageUserAsync(userId, action, request, metadata, ct)))
                .RequireCompanyContext().RequireAuthorization(ErpPermissions.IdentityUsersManage)
                .RequiresNonEmptyGuidPathParameter("userId").WithName(name).Accepts<ManageLocalUserV1>("application/json")
                .Produces<IdentityAccessCommandOutcomeV1>(200).ProducesErpCodeLessNotFound();
            UserCommandContract(endpoint);
        }
    }

    private static void UserCommandContract(RouteHandlerBuilder endpoint) => endpoint
        .WithIdempotencyContract(AccessCommandReplayableStatuses)
        .ProducesErpBadRequest(IdentityAccessProblemCodesV1.RequestInvalid, IdentityAccessProblemCodesV1.IdempotencyKeyInvalid,
            IdentityUserManagementV1.PolicyViolation)
        .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
        .ProducesErpConflict(IdentityAccessProblemCodesV1.IdempotencyConflict, IdentityAccessProblemCodesV1.VersionConflict,
            IdentityAccessProblemCodesV1.LastAdminRequired, IdentityAccessProblemCodesV1.UserNotAssignable,
            IdentityUserManagementV1.AssignmentRequired, IdentityUserManagementV1.UserNameTaken)
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
}
