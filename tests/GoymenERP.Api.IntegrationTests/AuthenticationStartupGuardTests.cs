using GoymenERP.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
}
