using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

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
        MapLocalAccountSession(group);

        return endpoints;
    }

    /// <summary>
    /// ID-1 local account session. Both routes are always in the document (the login screen is built from it) and
    /// answer a code-less 404 unless the host runs in the <c>LocalAccount</c> mode. Every login refusal is the same
    /// platform 401, whatever the reason (unknown user, wrong password, lock, inactive account); only the security
    /// event carries the reason. The rate limit is checked before any credential is read (KIM-D29). The login and
    /// every cookie-authenticated write carry the antiforgery header from the token route (KIM-D31,
    /// <see cref="LocalAccountAntiforgery"/>); logout renews the security stamp, so every cookie session of the user
    /// ends on its next request (KIM-D30).
    /// </summary>
    private static void MapLocalAccountSession(RouteGroupBuilder group)
    {
        group.MapGet("/session/antiforgery", async (
                HttpContext httpContext,
                IAuthenticationSchemeProvider schemeProvider) =>
            {
                NoStore(httpContext);
                if (await schemeProvider.GetSchemeAsync(LocalAccountSession.SchemeName) is null
                    || httpContext.RequestServices.GetService<IAntiforgery>() is not { } antiforgery)
                {
                    return NotFound();
                }

                // The request token is bound to the current user; a client asks again after login and logout.
                var tokens = antiforgery.GetAndStoreTokens(httpContext);
                return tokens.RequestToken is { Length: > 0 } requestToken
                    ? Results.Ok(new LocalAccountAntiforgeryTokenV1(requestToken))
                    : Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            })
            .AllowAnonymous()
            .WithName("GetLocalAccountAntiforgeryTokenV1")
            .Produces<LocalAccountAntiforgeryTokenV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpInternalError();

        group.MapPost("/session/login", async (
                LocalAccountLoginRequestV1 request,
                HttpContext httpContext,
                IAuthenticationSchemeProvider schemeProvider,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                if (await schemeProvider.GetSchemeAsync(LocalAccountSession.SchemeName) is null)
                {
                    return NotFound();
                }

                var services = httpContext.RequestServices;
                if (!services.GetRequiredService<LocalAccountLoginRateLimiter>().TryAcquire(httpContext, request.UserName))
                {
                    return LoginRateLimited(services.GetRequiredService<ICorrelationContext>());
                }

                var result = await services.GetRequiredService<ILocalAccountLoginService>().LoginAsync(
                    request.UserName,
                    request.Password,
                    cancellationToken);
                if (!result.Succeeded)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
                }

                await httpContext.SignInAsync(
                    LocalAccountSession.SchemeName,
                    LocalAccountSession.CreatePrincipal(result.UserId!.Value, result.SecurityStamp!.Value),
                    LocalAccountSession.CreateProperties(services.GetRequiredService<TimeProvider>().GetUtcNow()));
                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithMetadata(LocalAccountAntiforgery.Required)
            .WithName("LoginLocalAccountV1")
            .Accepts<LocalAccountLoginRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpLoginRefused()
            .ProducesErpCodeLessNotFound()
            .ProducesErpTooManyRequests(IdentitySessionProblemCodesV1.LoginRateLimited)
            .ProducesErpInternalError();

        group.MapPost("/session/logout", async (
                HttpContext httpContext,
                IAuthenticationSchemeProvider schemeProvider,
                CancellationToken cancellationToken) =>
            {
                NoStore(httpContext);
                if (await schemeProvider.GetSchemeAsync(LocalAccountSession.SchemeName) is null)
                {
                    return NotFound();
                }

                // KIM-D30: a new stamp ends this and every other cookie session of the user; there is no session table.
                // A stamp that already moved means the session had ended anyway, and the cookie is cleared either way.
                if (LocalAccountSession.TryReadSession(httpContext.User, out var userId, out var securityStamp))
                {
                    await httpContext.RequestServices.GetRequiredService<ILocalAccountLogoutService>()
                        .LogoutAsync(userId, securityStamp, cancellationToken);
                }

                await httpContext.SignOutAsync(LocalAccountSession.SchemeName);
                return Results.NoContent();
            })
            .WithName("LogoutLocalAccountV1")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();
    }

    private static IResult LoginRateLimited(ICorrelationContext correlationContext) => Results.Problem(
        title: "Çok fazla giriş denemesi.",
        detail: "Kısa süre içinde çok fazla giriş denemesi yapıldı. Lütfen biraz sonra yeniden deneyin.",
        statusCode: StatusCodes.Status429TooManyRequests,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = IdentitySessionProblemCodesV1.LoginRateLimited,
            ["correlationId"] = correlationContext.IsResolved ? correlationContext.CorrelationId : null,
        });

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
