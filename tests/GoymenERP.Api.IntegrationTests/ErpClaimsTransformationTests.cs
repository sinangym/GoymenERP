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

    [Theory]
    [InlineData("Bearer")]
    [InlineData("test-auth")]
    public async Task LocalIssuerClaimedByAnotherScheme_IsNeverResolvedToALocalUser(string authenticationType)
    {
        var userId = Guid.NewGuid();
        var foreignIdentity = new ClaimsIdentity(
            [
                new Claim("iss", "urn:goymen:local-account"),
                new Claim("sub", userId.ToString("D")),
            ],
            authenticationType);
        var resolver = new AlwaysResolvingIdentityResolver(userId);
        var transformation = new ErpClaimsTransformation(resolver);

        var transformed = await transformation.TransformAsync(
            new ClaimsPrincipal(foreignIdentity));

        Assert.Equal(0, resolver.Calls);
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == GoymenClaimTypes.UserId);
    }

    [Fact]
    public async Task LocalIssuerFromTheLocalSessionIdentity_IsResolved()
    {
        var userId = Guid.NewGuid();
        var localIdentity = new ClaimsIdentity(
            [
                new Claim("iss", "urn:goymen:local-account"),
                new Claim("sub", userId.ToString("D")),
            ],
            "LocalAccount");
        var resolver = new AlwaysResolvingIdentityResolver(userId);
        var transformation = new ErpClaimsTransformation(resolver);

        var transformed = await transformation.TransformAsync(
            new ClaimsPrincipal(localIdentity));

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(userId.ToString("D"), transformed.FindFirstValue(GoymenClaimTypes.UserId));
    }

    private sealed class AlwaysResolvingIdentityResolver(Guid userId) : IExternalIdentityResolver
    {
        public int Calls { get; private set; }

        public Task<ResolvedIdentity?> ResolveAsync(
            string issuer,
            string subject,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<ResolvedIdentity?>(new ResolvedIdentity(
                userId,
                "Sentetik Kullanıcı",
                Email: null,
                new HashSet<Guid>()));
        }
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
