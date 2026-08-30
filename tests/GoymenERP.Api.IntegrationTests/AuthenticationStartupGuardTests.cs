using GoymenERP.Api.Security;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AuthenticationStartupGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void DevelopmentBootstrap_IsRejectedOutsideDevelopmentAndTesting(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            mode: AuthenticationModes.DevelopmentBootstrap);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddErpAuthentication(
                configuration,
                new FakeHostEnvironment(environmentName)));

        Assert.Contains("yalnız Development ve Testing", exception.Message);
    }

    [Theory]
    [InlineData(AuthenticationModes.OidcJwtBearer, null, "goymen-erp-api")]
    [InlineData(AuthenticationModes.OidcJwtBearer, "https://identity.example.test", null)]
    [InlineData(null, null, null)]
    public void MissingOidcAuthorityOrAudience_IsRejected(
        string? mode,
        string? authority,
        string? audience)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(mode, authority, audience);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddErpAuthentication(
                configuration,
                new FakeHostEnvironment("Production")));

        Assert.Contains("Authority ve Audience zorunludur", exception.Message);
    }

    [Fact]
    public void UnsupportedAuthenticationMode_IsRejected()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(mode: "UnsupportedMode");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddErpAuthentication(
                configuration,
                new FakeHostEnvironment("Production")));

        Assert.Contains("Desteklenmeyen kimlik modu", exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task DevelopmentBootstrap_IsAcceptedOnlyAtExplicitDevelopmentBoundary(
        string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            mode: AuthenticationModes.DevelopmentBootstrap);

        services.AddErpAuthentication(
            configuration,
            new FakeHostEnvironment(environmentName));

        using var serviceProvider = services.BuildServiceProvider();
        var schemes = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var bootstrapScheme = await schemes.GetSchemeAsync(
            DevelopmentAuthenticationHandler.SchemeName);

        Assert.NotNull(bootstrapScheme);
        Assert.Equal(
            DevelopmentAuthenticationHandler.SchemeName,
            bootstrapScheme.Name);
    }

    [Fact]
    public void ValidProductionOidc_IsRegisteredWithoutRemoteDiscovery()
    {
        const string authority = "https://identity.example.test";
        const string audience = "goymen-erp-api";
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            AuthenticationModes.OidcJwtBearer,
            authority,
            audience);

        services.AddErpAuthentication(
            configuration,
            new FakeHostEnvironment("Production"));

        using var serviceProvider = services.BuildServiceProvider();
        var jwtOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(authority, jwtOptions.Authority);
        Assert.Equal(audience, jwtOptions.Audience);
        Assert.True(jwtOptions.RequireHttpsMetadata);
        Assert.False(jwtOptions.IncludeErrorDetails);
    }

    [Fact]
    public async Task InvalidProductionJwtChallenge_DoesNotExposeValidationDetails()
    {
        const string authority = "https://identity.example.test";
        const string audience = "goymen-erp-api";
        const string invalidToken = "token-validation-secret-not-a-jwt";
        var configuration = CreateConfiguration(
            AuthenticationModes.OidcJwtBearer,
            authority,
            audience);

        using var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddErpAuthentication(
                        configuration,
                        new FakeHostEnvironment("Production"));
                    services.RemoveAll<IClaimsTransformation>();
                    services.AddSingleton<IClaimsTransformation,
                        PassthroughClaimsTransformation>();
                    services.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options => options.Configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = authority,
                        });
                })
                .Configure(application =>
                {
                    application.UseAuthentication();
                    application.Run(async context =>
                    {
                        var result = await context.AuthenticateAsync();
                        if (!result.Succeeded)
                        {
                            await context.ChallengeAsync();
                        }
                    });
                }))
            .StartAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", invalidToken);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate).ToString();
        Assert.Equal("Bearer", challenge);
        Assert.DoesNotContain(invalidToken, challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("error_description", challenge, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration CreateConfiguration(
        string? mode = null,
        string? authority = null,
        string? audience = null)
    {
        var values = new Dictionary<string, string?>();

        if (mode is not null)
        {
            values["Authentication:Mode"] = mode;
        }

        if (authority is not null)
        {
            values["Authentication:Oidc:Authority"] = authority;
        }

        if (audience is not null)
        {
            values["Authentication:Oidc:Audience"] = audience;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(AuthenticationStartupGuardTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class PassthroughClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult(principal);
        }
    }
}
