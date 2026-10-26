using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Security;

public sealed class AuthenticationSessionBindingResolver(IHostEnvironment environment)
    : IAuthenticationSessionBindingResolver
{
    private const string ContractVersion = "goymen-authentication-session-v1";

    public string? Resolve(ClaimsPrincipal principal, Guid userId)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return null;
        }

        var bootstrapIdentities = principal.Identities
            .Where(identity => identity.IsAuthenticated
                && string.Equals(
                    identity.AuthenticationType,
                    DevelopmentAuthenticationHandler.SchemeName,
                    StringComparison.Ordinal))
            .ToArray();
        if (bootstrapIdentities.Length != 1)
        {
            return null;
        }

        var identity = bootstrapIdentities[0];
        var issuer = GetSingleClaim(identity, GoymenClaimTypes.IdentityIssuer);
        var subject = GetSingleClaim(identity, GoymenClaimTypes.IdentitySubject);
        var session = GetSingleClaim(identity, GoymenClaimTypes.AuthenticationSession);
        var localUserId = principal.FindAll(GoymenClaimTypes.UserId)
            .Select(claim => claim.Value)
            .ToArray();
        if (!string.Equals(issuer, BootstrapIdentityOptions.Issuer, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(subject)
            || !Guid.TryParseExact(session, "D", out var sessionId)
            || localUserId.Length != 1
            || !Guid.TryParseExact(localUserId[0], "D", out var claimedUserId)
            || claimedUserId != userId)
        {
            return null;
        }

        var source = string.Join(
            '\u001f',
            ContractVersion,
            issuer,
            subject,
            userId.ToString("D"),
            sessionId.ToString("D"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"v1.{Convert.ToHexStringLower(digest)}";
    }

    private static string? GetSingleClaim(ClaimsIdentity identity, string claimType)
    {
        var values = identity.FindAll(claimType).Select(claim => claim.Value).ToArray();
        return values.Length == 1 ? values[0] : null;
    }
}
