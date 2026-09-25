using System.Net;
using GoymenERP.Api.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DatabaseStartupModeTests
{
    [Fact]
    public void DefaultMode_PreservesInitialization()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Equal(
            DatabaseStartupMode.Initialize,
            DatabaseStartupModeResolver.Resolve(configuration, "Development"));
    }

    [Fact]
    public void ValidateOnly_IsRejectedOutsideDevelopmentAndTesting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartupModeResolver.ConfigurationKey] = "ValidateOnly",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseStartupModeResolver.Resolve(configuration, "Production"));

        Assert.Contains("yalnız Development ve Testing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateOnly_StartsWithoutDatabaseMutation_AndFailsReadinessClosed()
    {
        const string secret = "startup-readiness-must-not-leak";
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "DB_CONNECTION_STRING",
                    "Server=127.0.0.1,1;Database=missing;User Id=missing;"
                    + $"Password={secret};Encrypt=True;TrustServerCertificate=True;"
                    + "Connect Timeout=1;");
                builder.UseSetting(
                    DatabaseStartupModeResolver.ConfigurationKey,
                    "ValidateOnly");
            });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(
            "application/problem+json",
            ready.Content.Headers.ContentType?.MediaType);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("missing", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            DatabaseSchemaCompatibilityHealthCheck.Name,
            body,
            StringComparison.Ordinal);

        using var business = await client.GetAsync("/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, business.StatusCode);
        var businessBody = await business.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, businessBody, StringComparison.Ordinal);
        Assert.DoesNotContain("missing", businessBody, StringComparison.OrdinalIgnoreCase);
    }
}
