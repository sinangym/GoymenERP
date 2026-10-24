using System.Net;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Startup;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DatabaseSchemaCompatibilityCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readiness_covers_every_active_registered_context_without_requiring_dormant_Work(bool productEnabled)
    {
        Type[] registered = [];
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["DatabaseStartup:Mode"] = "ValidateOnly",
            ["ProductRuntime:IssuerLocalUserId"] = productEnabled ? Guid.NewGuid().ToString("D") : null,
            ["ProductRuntime:IssuerExternalIdentityId"] = productEnabled ? Guid.NewGuid().ToString("D") : null,
        };
        await using var parent = new GoymenErpApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder =>
        {
            // Program reads Product's options while composing services, before the later
            // app-configuration callback. Supply the same values at both host boundaries.
            foreach (var (key, value) in configurationOverrides)
                if (value is not null)
                    builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(configurationOverrides));
            builder.ConfigureTestServices(services => registered = services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
                .Select(descriptor => descriptor.ServiceType.GenericTypeArguments[0]).Distinct().ToArray());
        });
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(productEnabled, factory.Services.GetRequiredService<ProductRuntimeOptions>().IsEnabled);

        var activeRegistered = registered.Where(type => productEnabled
            || (type != typeof(ProductDbContext) && type != typeof(GroupSharedCommandDbContext))).ToArray();
        var covered = DatabaseSchemaCompatibilityHealthCheck.GetActiveContexts(productEnabled);
        Assert.Equal(activeRegistered.OrderBy(type => type.FullName).ToArray(),
            covered.Select(entry => entry.ContextType).OrderBy(type => type.FullName).ToArray());
        Assert.Equal(productEnabled ? 17 : 14, covered.Count);
        Assert.DoesNotContain(covered, entry => entry.ContextType.Name == "WorkDbContext");
    }
}
