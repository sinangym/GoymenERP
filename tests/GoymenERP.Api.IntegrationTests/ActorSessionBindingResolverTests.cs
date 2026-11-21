using System.Security.Claims;
using GoymenERP.Api.Security;
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

    [Fact]
    public async Task ProviderSelector_BindsLikeBearerWhenTheBearerSchemeAuthenticatedTheRequest()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedContext(userId);
        httpContext.Request.Headers.Authorization = "Bearer token-a";
        using var bearerServices = BuildSchemeProvider(JwtBearerDefaults.AuthenticationScheme);
        using var selectorServices = BuildSchemeProvider(ErpAuthenticationExtensions.ProviderSelectorScheme);
        using var protector = new ShellCapabilityGenerationProtector();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var bearerResolver = new ActorSessionBindingResolver(
            accessor,
            bearerServices.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);
        var selectorResolver = new ActorSessionBindingResolver(
            accessor,
            selectorServices.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);

        Assert.Null(await selectorResolver.ResolveAsync());

        httpContext.Features.Set<IAuthenticateResultFeature>(new RecordedAuthenticateResult
        {
            AuthenticateResult = AuthenticateResult.Success(
                new AuthenticationTicket(httpContext.User, JwtBearerDefaults.AuthenticationScheme)),
        });
        var expected = await bearerResolver.ResolveAsync();
        var actual = await selectorResolver.ResolveAsync();

        Assert.NotNull(expected);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("GoymenErpProviderSelector")]
    [InlineData("UnsupportedScheme")]
    public async Task ProviderSelector_HasNoBindingForANonBearerAuthenticatedScheme(string authenticatedScheme)
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedContext(userId);
        httpContext.Request.Headers.Authorization = "Bearer token-a";
        httpContext.Features.Set<IAuthenticateResultFeature>(new RecordedAuthenticateResult
        {
            AuthenticateResult = AuthenticateResult.Success(
                new AuthenticationTicket(httpContext.User, authenticatedScheme)),
        });
        using var services = BuildSchemeProvider(ErpAuthenticationExtensions.ProviderSelectorScheme);
        using var protector = new ShellCapabilityGenerationProtector();
        var resolver = new ActorSessionBindingResolver(
            new HttpContextAccessor { HttpContext = httpContext },
            services.GetRequiredService<IAuthenticationSchemeProvider>(),
            new FakeCurrentUser(userId),
            protector);

        Assert.Null(await resolver.ResolveAsync());
    }

    [Theory]
    [InlineData("LocalAccount")]
    [InlineData("GoymenErpProviderSelector")]
    public async Task LocalAccountSession_BindsToTheStampAndLoginInstantOfTheCookieTicket(string defaultScheme)
    {
        var userId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        using var services = BuildSchemeProvider(defaultScheme);
        using var protector = new ShellCapabilityGenerationProtector();
        var started = DateTimeOffset.Parse("2026-11-21T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        async Task<string?> ResolveAsync(Guid securityStamp, DateTimeOffset? startedAtUtc)
        {
            var httpContext = CreateLocalAccountContext(userId, securityStamp, startedAtUtc);
            var resolver = new ActorSessionBindingResolver(
                new HttpContextAccessor { HttpContext = httpContext },
                services.GetRequiredService<IAuthenticationSchemeProvider>(),
                new FakeCurrentUser(userId),
                protector);
            return await resolver.ResolveAsync();
        }

        var first = await ResolveAsync(stamp, started);
        var again = await ResolveAsync(stamp, started);
        var renewedStamp = await ResolveAsync(Guid.NewGuid(), started);
        var newLogin = await ResolveAsync(stamp, started.AddMinutes(5));

        Assert.NotNull(first);
        Assert.StartsWith("hmac-sha256:", first, StringComparison.Ordinal);
        Assert.Equal(first, again);
        Assert.NotNull(renewedStamp);
        Assert.NotEqual(first, renewedStamp);
        Assert.NotNull(newLogin);
        Assert.NotEqual(first, newLogin);
        Assert.DoesNotContain(stamp.ToString("D"), first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString("D"), first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalAccountSession_WithoutExactSessionMaterial_FailsClosed()
    {
        var userId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var started = DateTimeOffset.Parse("2026-11-21T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        using var services = BuildSchemeProvider(LocalAccountSession.SchemeName);
        using var protector = new ShellCapabilityGenerationProtector();

        async Task<string?> ResolveAsync(DefaultHttpContext httpContext) =>
            await new ActorSessionBindingResolver(
                new HttpContextAccessor { HttpContext = httpContext },
                services.GetRequiredService<IAuthenticationSchemeProvider>(),
                new FakeCurrentUser(userId),
                protector).ResolveAsync();

        // The login instant is missing from the ticket.
        Assert.Null(await ResolveAsync(CreateLocalAccountContext(userId, stamp, startedAtUtc: null)));

        // No recorded authentication result at all.
        var noResult = CreateLocalAccountContext(userId, stamp, started);
        noResult.Features.Set<IAuthenticateResultFeature>(null);
        Assert.Null(await ResolveAsync(noResult));

        // The ticket was issued by another scheme.
        var otherTicket = CreateLocalAccountContext(userId, stamp, started);
        otherTicket.Features.Set<IAuthenticateResultFeature>(new RecordedAuthenticateResult
        {
            AuthenticateResult = AuthenticateResult.Success(new AuthenticationTicket(
                otherTicket.User, StartedProperties(started), JwtBearerDefaults.AuthenticationScheme)),
        });
        Assert.Null(await ResolveAsync(otherTicket));

        // The principal carries no security stamp.
        var noStamp = CreateLocalAccountContext(userId, stamp, started);
        noStamp.User = new ClaimsPrincipal(new ClaimsIdentity(
            noStamp.User.Claims.Where(claim => claim.Type != LocalAccountSession.SecurityStampClaimType),
            LocalAccountSession.SchemeName));
        Assert.Null(await ResolveAsync(noStamp));

        // A principal that was not authenticated by the local scheme, even with the same claims.
        var foreignIdentity = CreateLocalAccountContext(userId, stamp, started);
        foreignIdentity.User = new ClaimsPrincipal(new ClaimsIdentity(foreignIdentity.User.Claims, "test-auth"));
        Assert.Null(await ResolveAsync(foreignIdentity));

        // Another issuer in the local claim pair.
        var foreignIssuer = CreateLocalAccountContext(userId, stamp, started);
        foreignIssuer.User = new ClaimsPrincipal(new ClaimsIdentity(
            foreignIssuer.User.Claims.Where(claim => claim.Type != LocalAccountSession.IssuerClaimType)
                .Append(new Claim(LocalAccountSession.IssuerClaimType, "https://identity.example.test")),
            LocalAccountSession.SchemeName));
        Assert.Null(await ResolveAsync(foreignIssuer));
    }

    private static DefaultHttpContext CreateLocalAccountContext(Guid userId, Guid securityStamp, DateTimeOffset? startedAtUtc)
    {
        var principal = LocalAccountSession.CreatePrincipal(userId, securityStamp);
        principal.AddIdentity(new ClaimsIdentity([new Claim(GoymenClaimTypes.UserId, userId.ToString("D"))]));
        var properties = startedAtUtc is { } started ? StartedProperties(started) : new AuthenticationProperties();
        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Features.Set<IAuthenticateResultFeature>(new RecordedAuthenticateResult
        {
            AuthenticateResult = AuthenticateResult.Success(
                new AuthenticationTicket(principal, properties, LocalAccountSession.SchemeName)),
        });
        return httpContext;
    }

    private static AuthenticationProperties StartedProperties(DateTimeOffset startedAtUtc) =>
        LocalAccountSession.CreateProperties(startedAtUtc);

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

    private sealed class RecordedAuthenticateResult : IAuthenticateResultFeature
    {
        public AuthenticateResult? AuthenticateResult { get; set; }
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
