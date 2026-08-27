using System.Security.Claims;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.Authentication;

namespace GoymenERP.Api.Security;

public sealed class ErpClaimsTransformation(IExternalIdentityResolver identityResolver)
    : IClaimsTransformation
{
    public const string LocalIdentityScheme = "GoymenErpLocalIdentity";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.Identities.Any(identity =>
                string.Equals(
                    identity.AuthenticationType,
                    LocalIdentityScheme,
                    StringComparison.Ordinal)
                && identity.HasClaim(claim => claim.Type == GoymenClaimTypes.UserId)))
        {
            return principal;
        }

        var isDevelopmentBootstrap = principal.Identities.Any(identity =>
            string.Equals(
                identity.AuthenticationType,
                DevelopmentAuthenticationHandler.SchemeName,
                StringComparison.Ordinal));
        var issuer = isDevelopmentBootstrap
            ? principal.FindFirstValue(GoymenClaimTypes.IdentityIssuer)
            : principal.FindFirstValue("iss");
        var subject = isDevelopmentBootstrap
            ? principal.FindFirstValue(GoymenClaimTypes.IdentitySubject)
            : principal.FindFirstValue("sub");

        var transformed = SanitizeUntrustedErpClaims(principal);
        var resolved = await identityResolver.ResolveAsync(
            issuer ?? string.Empty,
            subject ?? string.Empty);
        if (resolved is null)
        {
            return transformed;
        }

        var localClaims = new List<Claim>
        {
            new(GoymenClaimTypes.UserId, resolved.UserId.ToString("D")),
            new(ClaimTypes.Name, resolved.DisplayName),
        };
        localClaims.AddRange(resolved.AllowedCompanyIds.Select(companyId =>
            new Claim(GoymenClaimTypes.CompanyAccess, companyId.ToString("D"))));

        transformed.AddIdentity(new ClaimsIdentity(localClaims, LocalIdentityScheme));
        return transformed;
    }

    private static ClaimsPrincipal SanitizeUntrustedErpClaims(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Select(identity => new ClaimsIdentity(
            identity.Claims.Where(claim =>
                claim.Type != GoymenClaimTypes.UserId
                && claim.Type != GoymenClaimTypes.CompanyAccess),
            identity.AuthenticationType,
            identity.NameClaimType,
            identity.RoleClaimType));
        return new ClaimsPrincipal(identities);
    }
}
