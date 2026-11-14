using System.Globalization;
using System.Security.Claims;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GoymenERP.Api.Security;

/// <summary>
/// The local account session (spec "Oturum mekanizması", ASSUMPTION candidate: a server-validated cookie). The
/// cookie is <c>HttpOnly</c>, <c>Secure</c> and <c>SameSite=Strict</c> and holds only the claim pair
/// <c>(urn:goymen:local-account, UserId)</c> plus the security stamp; <see cref="ErpClaimsTransformation"/> resolves
/// permissions from the database as for every other identity (ADR-004). Every request re-reads the stamp; the idle
/// timeout slides and the absolute lifetime counts from the login, not from the last renewal.
/// </summary>
internal static class LocalAccountSession
{
    public const string SchemeName = "LocalAccount";

    /// <summary>The issuer of the local claim pair; the external identity link of a local user uses it.</summary>
    public const string Issuer = "urn:goymen:local-account";

    public const string IssuerClaimType = "iss";

    public const string SubjectClaimType = "sub";

    public const string SecurityStampClaimType = "goymen:local_security_stamp";

    /// <summary>The login instant (round-trip UTC) kept in the protected ticket; renewals do not move it.</summary>
    public const string StartedAtUtcItem = "goymen.local_session_started_utc";

    /// <summary>The <c>__Host-</c> prefix pins the cookie to this host, path <c>/</c> and HTTPS.</summary>
    public const string CookieName = "__Host-goymen-session";

    public static void Configure(CookieAuthenticationOptions options, LocalAccountSessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = settings.IdleTimeout!.Value;
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = ValidatePrincipalAsync,
            // An API answers with a status, never with a redirect to a login page.
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnRedirectToLogout = context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            OnRedirectToReturnUrl = context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
        };
    }

    public static ClaimsPrincipal CreatePrincipal(Guid userId, Guid securityStamp) =>
        new(new ClaimsIdentity(
            [
                new Claim(IssuerClaimType, Issuer),
                new Claim(SubjectClaimType, userId.ToString("D")),
                new Claim(SecurityStampClaimType, securityStamp.ToString("D")),
            ],
            SchemeName));

    public static AuthenticationProperties CreateProperties(DateTimeOffset startedAtUtc)
    {
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true,
        };
        properties.Items[StartedAtUtcItem] = startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return properties;
    }

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var settings = services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAccountOptions>>()
            .Value.Session;
        var nowUtc = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var principal = context.Principal;

        if (principal is null
            || !string.Equals(principal.FindFirstValue(IssuerClaimType), Issuer, StringComparison.Ordinal)
            || !Guid.TryParseExact(principal.FindFirstValue(SubjectClaimType), "D", out var userId)
            || !Guid.TryParseExact(principal.FindFirstValue(SecurityStampClaimType), "D", out var securityStamp)
            || !context.Properties.Items.TryGetValue(StartedAtUtcItem, out var startedText)
            || !DateTimeOffset.TryParseExact(
                startedText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var startedAtUtc)
            || settings.AbsoluteLifetime is not { } absoluteLifetime
            || nowUtc >= startedAtUtc + absoluteLifetime
            || !await services.GetRequiredService<ILocalAccountSessionValidator>().IsCurrentAsync(
                userId,
                securityStamp,
                context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(SchemeName);
        }
    }
}
