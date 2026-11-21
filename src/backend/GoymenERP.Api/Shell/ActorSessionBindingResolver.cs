using System.Security.Claims;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Shell;

internal sealed class ActorSessionBindingResolver(
    IHttpContextAccessor httpContextAccessor,
    IAuthenticationSchemeProvider schemeProvider,
    ICurrentUser currentUser,
    ShellCapabilityGenerationProtector protector)
    : IActorSessionBindingResolver
{
    public async Task<string?> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return null;
        }

        var scheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
        if (scheme is null)
        {
            return null;
        }

        var schemeName = scheme.Name;
        if (string.Equals(
                schemeName,
                ErpAuthenticationExtensions.ProviderSelectorScheme,
                StringComparison.Ordinal))
        {
            // ID-6: behind the provider selector the binding follows the scheme that actually authenticated this
            // request; without a recorded result there is no binding.
            schemeName = httpContext.Features
                .Get<IAuthenticateResultFeature>()?
                .AuthenticateResult?
                .Ticket?
                .AuthenticationScheme;
            if (schemeName is null)
            {
                return null;
            }
        }

        if (string.Equals(
                schemeName,
                DevelopmentAuthenticationHandler.SchemeName,
                StringComparison.Ordinal))
        {
            return ResolveDevelopmentBinding(httpContext.User, schemeName);
        }

        if (string.Equals(
                schemeName,
                JwtBearerDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            return ResolveBearerBinding(httpContext, schemeName);
        }

        if (string.Equals(
                schemeName,
                LocalAccountSession.SchemeName,
                StringComparison.Ordinal))
        {
            return ResolveLocalAccountBinding(httpContext, schemeName);
        }

        return null;
    }

    /// <summary>
    /// F-001 local session (cookie). The binding is the exact session the cookie ticket carries: the local claim pair,
    /// the security stamp that a logout or password change renews, and the login instant kept in the protected ticket,
    /// so a new login or a renewed stamp is a new binding. Any missing piece fails closed.
    /// </summary>
    private string? ResolveLocalAccountBinding(
        HttpContext httpContext,
        string schemeName)
    {
        var principal = httpContext.User;
        if (!principal.Identities.Any(identity => identity.IsAuthenticated && string.Equals(
                identity.AuthenticationType,
                LocalAccountSession.SchemeName,
                StringComparison.Ordinal))
            || !TryReadSingleClaim(
                principal,
                LocalAccountSession.IssuerClaimType,
                out var issuer)
            || !string.Equals(issuer, LocalAccountSession.Issuer, StringComparison.Ordinal)
            || !TryReadSingleClaim(
                principal,
                LocalAccountSession.SubjectClaimType,
                out var subject)
            || !TryReadSingleClaim(
                principal,
                LocalAccountSession.SecurityStampClaimType,
                out var securityStamp)
            || httpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult
                is not { Succeeded: true, Ticket: { } ticket }
            || !string.Equals(ticket.AuthenticationScheme, schemeName, StringComparison.Ordinal)
            || !ticket.Properties.Items.TryGetValue(LocalAccountSession.StartedAtUtcItem, out var startedAtUtc)
            || string.IsNullOrWhiteSpace(startedAtUtc))
        {
            return null;
        }

        return protector.Protect(
            ["local-account-session", schemeName, issuer, subject, currentUser.UserId!, securityStamp, startedAtUtc]);
    }

    private string? ResolveDevelopmentBinding(
        ClaimsPrincipal principal,
        string schemeName)
    {
        if (!principal.Identities.Any(identity => string.Equals(
                identity.AuthenticationType,
                DevelopmentAuthenticationHandler.SchemeName,
                StringComparison.Ordinal))
            || !TryReadSingleClaim(
                principal,
                GoymenClaimTypes.IdentityIssuer,
                out var issuer)
            || !TryReadSingleClaim(
                principal,
                GoymenClaimTypes.IdentitySubject,
                out var subject))
        {
            return null;
        }

        return protector.Protect(
            ["development-bootstrap", schemeName, issuer, subject, currentUser.UserId!]);
    }

    private string? ResolveBearerBinding(
        HttpContext httpContext,
        string schemeName)
    {
        if (!httpContext.Request.Headers.TryGetValue(
                HeaderNames.Authorization,
                out var values)
            || values.Count != 1
            || values[0] is not { } rawHeader
            || !rawHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = rawHeader["Bearer ".Length..];
        if (token.Length == 0
            || !string.Equals(token, token.Trim(), StringComparison.Ordinal)
            || token.Any(char.IsWhiteSpace)
            || token.Contains(',', StringComparison.Ordinal)
            || !TryReadSingleClaim(httpContext.User, "iss", out var issuer)
            || !TryReadSingleClaim(httpContext.User, "sub", out var subject))
        {
            return null;
        }

        return protector.Protect(
            ["validated-bearer", schemeName, issuer, subject, currentUser.UserId!, token]);
    }

    private static bool TryReadSingleClaim(
        ClaimsPrincipal principal,
        string claimType,
        out string value)
    {
        var values = principal.FindAll(claimType)
            .Select(claim => claim.Value)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 1)
        {
            value = values[0];
            return true;
        }

        value = string.Empty;
        return false;
    }
}
