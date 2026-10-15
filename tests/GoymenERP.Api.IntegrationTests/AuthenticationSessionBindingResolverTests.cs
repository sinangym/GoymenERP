using System.Net;
using System.Security.Claims;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AuthenticationSessionBindingResolverTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string SessionHeaderName = "X-Development-Session";

    [Fact]
    public async Task DevelopmentSessionBinding_IsStableAcrossRequestsAndResolverInstances()
    {
        var sessionId = Guid.NewGuid();
        using var client = CreateClient(TestUsers.CompanyA, sessionId.ToString("D"));

        var first = await GetBindingAsync(client);
        var second = await GetBindingAsync(client);
        var principal = CreateBootstrapPrincipal(TestUsers.CompanyA, sessionId);
        var userId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var directFirst = new AuthenticationSessionBindingResolver(
            new FakeHostEnvironment("Testing")).Resolve(principal, userId);
        var directSecond = new AuthenticationSessionBindingResolver(
            new FakeHostEnvironment("Testing")).Resolve(principal, userId);

        Assert.NotNull(first);
        Assert.DoesNotContain(sessionId.ToString("D"), first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first, second);
        Assert.Equal(directFirst, directSecond);
        Assert.Equal(first, directFirst);
    }

    [Fact]
    public async Task SameActorWithDifferentSessions_ProducesIsolatedBindings()
    {
        var sessionA = Guid.NewGuid().ToString("D");
        var sessionB = Guid.NewGuid().ToString("D");
        using var firstA = CreateClient(TestUsers.CompanyA, sessionA);
        using var clientB = CreateClient(TestUsers.CompanyA, sessionB);
        using var secondA = CreateClient(TestUsers.CompanyA, sessionA);

        var bindingA = await GetBindingAsync(firstA);
        var bindingB = await GetBindingAsync(clientB);
        var repeatedBindingA = await GetBindingAsync(secondA);

        Assert.NotNull(bindingA);
        Assert.NotNull(bindingB);
        Assert.NotEqual(bindingA, bindingB);
        Assert.Equal(bindingA, repeatedBindingA);
    }

    [Fact]
    public async Task ActorChangeWithSameSession_DoesNotReuseAnotherActorsBinding()
    {
        var session = Guid.NewGuid().ToString("D");
        using var firstA = CreateClient(TestUsers.CompanyA, session);
        using var clientB = CreateClient(TestUsers.CompanyB, session);
        using var secondA = CreateClient(TestUsers.CompanyA, session);

        var bindingA = await GetBindingAsync(firstA);
        var bindingB = await GetBindingAsync(clientB);
        var repeatedBindingA = await GetBindingAsync(secondA);

        Assert.NotNull(bindingA);
        Assert.NotNull(bindingB);
        Assert.NotEqual(bindingA, bindingB);
        Assert.Equal(bindingA, repeatedBindingA);
    }

    [Fact]
    public async Task MissingMalformedOrMultipleSessionHeaders_ReturnNullableBinding()
    {
        using var missing = factory.CreateClientFor(TestUsers.CompanyA);
        using var malformed = CreateClient(TestUsers.CompanyA, "not-a-session");
        using var multiple = factory.CreateClientFor(TestUsers.CompanyA);
        multiple.DefaultRequestHeaders.TryAddWithoutValidation(
            SessionHeaderName,
            [Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")]);

        Assert.Null(await GetBindingAsync(missing));
        Assert.Null(await GetBindingAsync(malformed));
        Assert.Null(await GetBindingAsync(multiple));
    }

    [Theory]
    [InlineData("Production", DevelopmentAuthenticationHandler.SchemeName)]
    [InlineData("Staging", DevelopmentAuthenticationHandler.SchemeName)]
    [InlineData("Testing", JwtBearerDefaults.AuthenticationScheme)]
    public void UntrustedEnvironmentOrAuthenticationScheme_ReturnsNull(
        string environmentName,
        string authenticationType)
    {
        var resolver = new AuthenticationSessionBindingResolver(
            new FakeHostEnvironment(environmentName));
        var principal = CreatePrincipal(
            authenticationType,
            TestUsers.CompanyA,
            Guid.NewGuid());

        Assert.Null(resolver.Resolve(
            principal,
            Guid.Parse("20000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public void MissingOrDuplicateTrustedClaims_ReturnsNull()
    {
        var environment = new FakeHostEnvironment("Testing");
        var resolver = new AuthenticationSessionBindingResolver(environment);
        var sessionId = Guid.NewGuid();
        var userId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var missing = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GoymenClaimTypes.IdentityIssuer, BootstrapIdentityOptions.Issuer),
                new Claim(GoymenClaimTypes.IdentitySubject, TestUsers.CompanyA),
            ],
            DevelopmentAuthenticationHandler.SchemeName));
        var duplicate = CreateBootstrapPrincipal(TestUsers.CompanyA, sessionId);
        duplicate.Identities.Single().AddClaim(new Claim(
            GoymenClaimTypes.AuthenticationSession,
            sessionId.ToString("D")));

        Assert.Null(resolver.Resolve(missing, userId));
        Assert.Null(resolver.Resolve(duplicate, userId));
    }

    private HttpClient CreateClient(string userId, string sessionId)
    {
        var client = factory.CreateClientFor(userId);
        client.DefaultRequestHeaders.TryAddWithoutValidation(SessionHeaderName, sessionId);
        return client;
    }

    private static async Task<string?> GetBindingAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/identity/me");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var binding = document.RootElement.GetProperty("authenticationSessionBinding");
        return binding.ValueKind == JsonValueKind.Null ? null : binding.GetString();
    }

    private static ClaimsPrincipal CreateBootstrapPrincipal(string subject, Guid sessionId) =>
        CreatePrincipal(DevelopmentAuthenticationHandler.SchemeName, subject, sessionId);

    private static ClaimsPrincipal CreatePrincipal(
        string authenticationType,
        string subject,
        Guid sessionId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GoymenClaimTypes.IdentityIssuer, BootstrapIdentityOptions.Issuer),
                new Claim(GoymenClaimTypes.IdentitySubject, subject),
                new Claim(GoymenClaimTypes.AuthenticationSession, sessionId.ToString("D")),
                new Claim(
                    GoymenClaimTypes.UserId,
                    string.Equals(subject, TestUsers.CompanyB, StringComparison.Ordinal)
                        ? "20000000-0000-0000-0000-000000000002"
                        : "20000000-0000-0000-0000-000000000001"),
            ],
            authenticationType));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(AuthenticationSessionBindingResolverTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
