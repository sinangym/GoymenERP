using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity")
            .RequireAuthorization()
            .WithTags("Identity");

        group.MapGet("/me", async (
            HttpContext httpContext,
            IIdentityProfileService profileService,
            IAuthenticationSessionBindingResolver sessionBindingResolver,
            CancellationToken cancellationToken) =>
        {
            NoStore(httpContext);
            var profile = await profileService.GetCurrentAsync(cancellationToken);
            return profile is null
                ? Results.Problem(
                    title: "Kimlik bulunamadı.",
                    detail: "Oturum kimliği etkin bir ERP kullanıcısıyla eşleşmiyor.",
                    statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(profile with
                {
                    AuthenticationSessionBinding = sessionBindingResolver.Resolve(
                        httpContext.User,
                        profile.UserId),
                });
        })
            .WithName("GetCurrentIdentityV1")
            .Produces<IdentityProfile>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        MapAccessView(group);

        return endpoints;
    }

    /// <summary>
    /// AT-004 read-only access view ("Kullanıcılar ve yetkiler"). Four GETs, all on
    /// <see cref="IdentityPermissionManifestV1.AssignmentsRead"/> in the server-resolved company, all re-authorized
    /// company-wide by the service. An unknown id, another company's user and a user with no assignment here are one
    /// code-less 404, never a 403 that would confirm the user exists. Responses are private and never cached. There is
    /// deliberately no write route: assignment and revocation are blocked (NEEDS-SINAN #66 5738113623, OPEN-013).
    /// </summary>
    private static void MapAccessView(RouteGroupBuilder group)
    {
        group.MapGet("/access/users", async (
                HttpContext httpContext,
                IIdentityAccessReadService service,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                return Results.Ok(await service.GetUsersAsync(cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AssignmentsRead)
            .WithName("GetIdentityAccessUsersV1")
            .Produces<IReadOnlyList<IdentityAccessUserSummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/access/users/{userId:guid}", async (
                Guid userId,
                HttpContext httpContext,
                IIdentityAccessReadService service,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                var user = await service.GetUserAsync(userId, cancellationToken);
                return user is null ? NotFound() : Results.Ok(user);
            })
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AssignmentsRead)
            .WithName("GetIdentityAccessUserV1")
            .Produces<IdentityAccessUserDetail>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/access/roles", async (
                HttpContext httpContext,
                IIdentityAccessReadService service,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                return Results.Ok(await service.GetRolesAsync(cancellationToken));
            })
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AssignmentsRead)
            .WithName("GetIdentityAccessRolesV1")
            .Produces<IReadOnlyList<IdentityAccessRoleSummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/access/roles/{roleId:guid}", async (
                Guid roleId,
                HttpContext httpContext,
                IIdentityAccessReadService service,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                var role = await service.GetRoleAsync(roleId, cancellationToken);
                return role is null ? NotFound() : Results.Ok(role);
            })
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AssignmentsRead)
            .WithName("GetIdentityAccessRoleV1")
            .Produces<IdentityAccessRoleDetail>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();
    }

    private static void NoStore(HttpContext httpContext) =>
        httpContext.Response.Headers.CacheControl = "private, no-store";

    private static IResult NotFound() => Results.Problem(
        title: "Kayıt bulunamadı.",
        detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
        statusCode: StatusCodes.Status404NotFound);
}
