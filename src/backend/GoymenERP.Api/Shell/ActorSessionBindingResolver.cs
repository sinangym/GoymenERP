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

        if (string.Equals(
                scheme.Name,
                DevelopmentAuthenticationHandler.SchemeName,
                StringComparison.Ordinal))
        {
            return ResolveDevelopmentBinding(httpContext.User, scheme.Name);
        }

        if (string.Equals(
                scheme.Name,
                JwtBearerDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            return ResolveBearerBinding(httpContext, scheme.Name);
        }

        return null;
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
