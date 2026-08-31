using System.Security.Claims;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ActorSessionBindingResolverTests
{
    [Fact]
    public async Task ValidatedBearerBinding_RequiresExactTokenAndChangesWithToken()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedContext(userId);
        httpContext.Request.Headers.Authorization = "Bearer token-a";
        using var services = BuildSchemeProvider(JwtBearerDefaults.AuthenticationScheme);
        using var protector = new ShellCapabilityGenerationProtector();
        var resolver = new ActorSessionBindingResolver(
            new HttpContextAccessor { HttpContext = httpContext },
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);

        var first = await resolver.ResolveAsync();
        httpContext.Request.Headers.Authorization = "Bearer token-b";
        var second = await resolver.ResolveAsync();

        Assert.NotNull(first);
        Assert.StartsWith("hmac-sha256:", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("token-a", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic credential")]
    [InlineData("Bearer ")]
    [InlineData("Bearer token with whitespace")]
    public async Task ProductionBearerWithoutExactSessionMaterial_FailsClosed(
        string? authorization)
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedContext(userId);
        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        using var services = BuildSchemeProvider(JwtBearerDefaults.AuthenticationScheme);
        using var protector = new ShellCapabilityGenerationProtector();
        var resolver = new ActorSessionBindingResolver(
            new HttpContextAccessor { HttpContext = httpContext },
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);

        Assert.Null(await resolver.ResolveAsync());
    }

    [Fact]
    public async Task UnsupportedAuthenticationScheme_HasNoActorOnlyFallback()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedContext(userId);
        using var services = BuildSchemeProvider("UnsupportedScheme");
        using var protector = new ShellCapabilityGenerationProtector();
        var resolver = new ActorSessionBindingResolver(
            new HttpContextAccessor { HttpContext = httpContext },
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);

        Assert.Null(await resolver.ResolveAsync());
    }

    private static DefaultHttpContext CreateAuthenticatedContext(Guid userId)
    {
        var claims = new[]
        {
            new Claim("iss", "https://identity.example.test"),
            new Claim("sub", "external-subject"),
            new Claim(GoymenClaimTypes.UserId, userId.ToString("D")),
        };
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-auth")),
        };
    }

    private static ServiceProvider BuildSchemeProvider(string scheme)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(scheme)
            .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>(
                scheme,
                _ => { });
        return services.BuildServiceProvider();
    }

    private sealed class FakeCurrentUser(Guid userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => userId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>();

        public bool CanAccessCompany(Guid companyId) => false;
    }

    private sealed class NoOpAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
