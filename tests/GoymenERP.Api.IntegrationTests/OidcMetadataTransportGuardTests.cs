using GoymenERP.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OidcMetadataTransportGuardTests
{
    [Theory]
    [InlineData("Production", "http://identity.example.test")]
    [InlineData("Staging", "identity.example.test")]
    [InlineData("QualityAssurance", "/identity")]
    [InlineData("PreProduction", "ftp://identity.example.test")]
    [InlineData("Sandbox", "https:identity.example.test")]
    [InlineData("Production", "https://trusted.example:443@evil.example")]
    [InlineData("Staging", "https://identity.example.test#fragment")]
    [InlineData("QualityAssurance", "https://identity.example.test?x=http://evil")]
    public void NonDevelopmentOidc_RejectsAuthorityThatIsNotAbsoluteHttps(
        string environmentName,
        string authority)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            authority,
            requireHttpsMetadata: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddErpAuthentication(
                configuration,
                new FakeHostEnvironment(environmentName)));

        Assert.Contains("mutlak bir HTTPS adresi", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QualityAssurance")]
    public void NonDevelopmentOidc_RejectsDisabledHttpsMetadata(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            "https://identity.example.test",
            requireHttpsMetadata: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddErpAuthentication(
                configuration,
                new FakeHostEnvironment(environmentName)));

        Assert.Contains("RequireHttpsMetadata", exception.Message);
        Assert.Contains("true olmalıdır", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QualityAssurance")]
    public void NonDevelopmentOidc_RegistersHttpsAuthorityWithPathWithoutRemoteDiscovery(
        string environmentName)
    {
        const string authority = "https://identity.example.test/tenant/goymen";
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            authority,
            requireHttpsMetadata: true);

        services.AddErpAuthentication(
            configuration,
            new FakeHostEnvironment(environmentName));

        using var serviceProvider = services.BuildServiceProvider();
        var jwtOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(authority, jwtOptions.Authority);
        Assert.Equal("goymen-erp-api", jwtOptions.Audience);
        Assert.True(jwtOptions.RequireHttpsMetadata);
    }

    [Fact]
    public void DevelopmentOidc_AllowsExplicitHttpMetadataConfiguration()
    {
        const string authority = "http://localhost:8080";
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            authority,
            requireHttpsMetadata: false);

        services.AddErpAuthentication(
            configuration,
            new FakeHostEnvironment("Development"));

        using var serviceProvider = services.BuildServiceProvider();
        var jwtOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(authority, jwtOptions.Authority);
        Assert.False(jwtOptions.RequireHttpsMetadata);
    }

    [Fact]
    public async Task Testing_KeepsExplicitDevelopmentBootstrapBoundary()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            "http://localhost:8080",
            requireHttpsMetadata: false);

        services.AddErpAuthentication(
            configuration,
            new FakeHostEnvironment("Testing"));

        using var serviceProvider = services.BuildServiceProvider();
        var schemes = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.NotNull(await schemes.GetSchemeAsync(DevelopmentAuthenticationHandler.SchemeName));
        Assert.Null(await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
    }

    private static IConfiguration CreateConfiguration(
        string authority,
        bool requireHttpsMetadata)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = AuthenticationModes.OidcJwtBearer,
            ["Authentication:Oidc:Authority"] = authority,
            ["Authentication:Oidc:Audience"] = "goymen-erp-api",
            ["Authentication:Oidc:RequireHttpsMetadata"] = requireHttpsMetadata.ToString(),
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(OidcMetadataTransportGuardTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
