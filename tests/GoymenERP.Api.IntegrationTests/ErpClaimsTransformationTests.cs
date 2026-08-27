using System.Security.Claims;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ErpClaimsTransformationTests
{
    [Fact]
    public async Task UntrustedOidcErpClaims_CannotBypassLocalProvisioning()
    {
        var externalIdentity = new ClaimsIdentity(
            [
                new Claim("iss", "https://identity.example.test"),
                new Claim("sub", "external-subject"),
                new Claim(GoymenClaimTypes.UserId, Guid.NewGuid().ToString("D")),
                new Claim(GoymenClaimTypes.CompanyAccess, Guid.NewGuid().ToString("D")),
            ],
            "Bearer");
        var transformation = new ErpClaimsTransformation(new NullIdentityResolver());

        var transformed = await transformation.TransformAsync(
            new ClaimsPrincipal(externalIdentity));

        Assert.DoesNotContain(
            transformed.Claims,
            claim => claim.Type == GoymenClaimTypes.UserId
                || claim.Type == GoymenClaimTypes.CompanyAccess);
        Assert.DoesNotContain(
            transformed.Identities,
            identity => identity.AuthenticationType == ErpClaimsTransformation.LocalIdentityScheme);
    }

    private sealed class NullIdentityResolver : IExternalIdentityResolver
    {
        public Task<ResolvedIdentity?> ResolveAsync(
            string issuer,
            string subject,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ResolvedIdentity?>(null);
        }
    }
}
