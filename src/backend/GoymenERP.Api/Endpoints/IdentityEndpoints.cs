using System.Globalization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace GoymenERP.Api.Endpoints;

public static class IdentityEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";

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
            .WithMetadata(LocalAccountPasswordChangeFence.Allowed)
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
            .WithMetadata(LocalAccountPasswordChangeFence.Allowed)
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
            .WithMetadata(LocalAccountPasswordChangeFence.Allowed)
            .WithName("LogoutLocalAccountV1")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        MapLocalAccountPasswordChange(group);
    }

    /// <summary>
    /// ID-2 <c>POST /session/password</c>: the session's own password change, also the only write a session with
    /// <c>MustChangePassword=true</c> may make (<see cref="LocalAccountPasswordChangeFence"/>). It needs the antiforgery
    /// header (KIM-D31), an <c>Idempotency-Key</c> and the expected <c>localCredentialVersion</c> from <c>/me</c>. The
    /// fingerprint never contains a password. Success renews the security stamp, which ends every other session of the
    /// user (KIM-D30), and re-issues this session's cookie with the new stamp and the original start, so the absolute
    /// lifetime does not restart. A wrong current password is a 400 that never counts toward the lockout; a session
    /// whose stamp moved is the platform 401.
    /// </summary>
    private static void MapLocalAccountPasswordChange(RouteGroupBuilder group)
    {
        group.MapPost("/session/password", async (
                LocalAccountPasswordChangeRequestV1 request,
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
                var correlationContext = services.GetRequiredService<ICorrelationContext>();
                if (!LocalAccountSession.TryReadSession(httpContext.User, out var userId, out var securityStamp))
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
                }

                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey))
                {
                    return PasswordChangeProblem(
                        StatusCodes.Status400BadRequest,
                        IdentitySessionProblemCodesV1.IdempotencyKeyInvalid,
                        correlationContext,
                        field: "idempotencyKey");
                }

                if (string.IsNullOrEmpty(request.CurrentPassword)
                    || request.NewPassword is null
                    || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
                    || expectedVersion < 1)
                {
                    return PasswordChangeProblem(
                        StatusCodes.Status400BadRequest,
                        IdentitySessionProblemCodesV1.RequestInvalid,
                        correlationContext,
                        field: string.IsNullOrEmpty(request.CurrentPassword) ? "currentPassword" : "expectedVersion");
                }

                var result = await services.GetRequiredService<ILocalAccountPasswordChangeService>().ChangeAsync(
                    new LocalAccountPasswordChangeCommand(
                        userId,
                        securityStamp,
                        idempotencyKey,
                        expectedVersion,
                        request.CurrentPassword,
                        request.NewPassword),
                    cancellationToken);
                switch (result.Status)
                {
                    case LocalAccountPasswordChangeStatus.Changed when result.NewSecurityStamp is { } newStamp:
                        var authentication = await httpContext.AuthenticateAsync(LocalAccountSession.SchemeName);
                        var startedAtUtc = LocalAccountSession.TryReadStartedAtUtc(authentication.Properties, out var started)
                            ? started
                            : services.GetRequiredService<TimeProvider>().GetUtcNow();
                        await httpContext.SignInAsync(
                            LocalAccountSession.SchemeName,
                            LocalAccountSession.CreatePrincipal(userId, newStamp),
                            LocalAccountSession.CreateProperties(startedAtUtc));
                        return Results.NoContent();
                    case LocalAccountPasswordChangeStatus.Replayed:
                        httpContext.Response.Headers[IdempotencyReplayedHeaderName] = "true";
                        return Results.NoContent();
                    case LocalAccountPasswordChangeStatus.PolicyViolation:
                        return PasswordChangeProblem(
                            StatusCodes.Status400BadRequest,
                            IdentitySessionProblemCodesV1.PasswordPolicyViolation,
                            correlationContext,
                            field: "newPassword",
                            violations: result.PolicyViolations);
                    case LocalAccountPasswordChangeStatus.CurrentPasswordInvalid:
                        return PasswordChangeProblem(
                            StatusCodes.Status400BadRequest,
                            IdentitySessionProblemCodesV1.CurrentPasswordInvalid,
                            correlationContext,
                            field: "currentPassword");
                    case LocalAccountPasswordChangeStatus.VersionConflict:
                        return PasswordChangeProblem(
                            StatusCodes.Status409Conflict,
                            IdentitySessionProblemCodesV1.VersionConflict,
                            correlationContext,
                            expectedVersion: expectedVersion,
                            currentVersion: result.CurrentVersion);
                    case LocalAccountPasswordChangeStatus.IdempotencyConflict:
                        return PasswordChangeProblem(
                            StatusCodes.Status409Conflict,
                            IdentitySessionProblemCodesV1.IdempotencyConflict,
                            correlationContext);
                    default:
                        // SessionNotCurrent (or a Changed result without a stamp): the session ends.
                        await httpContext.SignOutAsync(LocalAccountSession.SchemeName);
                        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .WithMetadata(LocalAccountAntiforgery.Required)
            .WithMetadata(LocalAccountPasswordChangeFence.Allowed)
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict)
            .WithName("ChangeLocalAccountPasswordV1")
            .Accepts<LocalAccountPasswordChangeRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                IdentitySessionProblemCodesV1.AntiforgeryTokenInvalid,
                IdentitySessionProblemCodesV1.IdempotencyKeyInvalid,
                IdentitySessionProblemCodesV1.RequestInvalid,
                IdentitySessionProblemCodesV1.PasswordPolicyViolation,
                IdentitySessionProblemCodesV1.CurrentPasswordInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                IdentitySessionProblemCodesV1.VersionConflict,
                IdentitySessionProblemCodesV1.IdempotencyConflict)
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();
    }

    private static IResult PasswordChangeProblem(
        int statusCode,
        string code,
        ICorrelationContext correlationContext,
        string? field = null,
        IReadOnlyList<string>? violations = null,
        long? expectedVersion = null,
        long? currentVersion = null)
    {
        var (title, detail) = code switch
        {
            IdentitySessionProblemCodesV1.PasswordPolicyViolation =>
                ("Parola politikası karşılanmadı.", "Yeni parola parola politikasını karşılamıyor."),
            IdentitySessionProblemCodesV1.CurrentPasswordInvalid =>
                ("Mevcut parola hatalı.", "Mevcut parola doğrulanamadı."),
            IdentitySessionProblemCodesV1.VersionConflict =>
                ("Kayıt değişti.", "Hesap bilgileri başka bir işlemle değişti; güncel bilgiyle yeniden deneyin."),
            IdentitySessionProblemCodesV1.IdempotencyConflict =>
                ("İstek anahtarı kullanılmış.", "Bu istek anahtarı farklı bir istekle kullanılmış."),
            IdentitySessionProblemCodesV1.IdempotencyKeyInvalid =>
                ("İstek anahtarı geçersiz.", "Idempotency-Key başlığı eksik veya geçersiz."),
            _ => ("İstek geçersiz.", "İstek alanları eksik veya geçersiz."),
        };
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = correlationContext.IsResolved ? correlationContext.CorrelationId : null,
        };
        if (field is not null)
        {
            extensions["field"] = field;
        }

        if (violations is { Count: > 0 })
        {
            extensions["violations"] = violations;
        }

        if (expectedVersion is { } expected && currentVersion is { } current)
        {
            extensions["expectedVersion"] = expected.ToString(CultureInfo.InvariantCulture);
            extensions["currentVersion"] = current.ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(title: title, detail: detail, statusCode: statusCode, extensions: extensions);
    }

    /// <summary>The house rule (as in the accounting writes): one header value, trimmed, 1..128 characters, no comma.</summary>
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values) || values.Count != 1
            || values[0] is not { } rawValue)
        {
            return false;
        }

        var canonical = rawValue.Trim();
        if (canonical.Length is 0 or > LocalCredentialCommand.IdempotencyKeyMaxLength
            || canonical.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        idempotencyKey = canonical;
        return true;
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
