using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Product.UnitTests;

/// <summary>Host composition of the Product sales-eligibility runtime (central contribution).</summary>
public sealed class ProductSalesEligibilityCompositionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Host_composition_without_runtime_or_settings_provider_is_unavailable(bool configured, bool settings)
    {
        await using var provider = Host(configured, settings);
        await using var scope = provider.CreateAsyncScope();
        var eligibility = scope.ServiceProvider.GetRequiredService<IProductSalesEligibilityV1>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => eligibility.EvaluateAsync(ProductSalesEligibilityServiceTests.Selection, default));
        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task Configured_host_composes_the_owner_runtime_with_the_fail_closed_family_policy()
    {
        await using var provider = Host(configured: true, settings: true);
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<ProductSalesEligibilityService>(scope.ServiceProvider.GetRequiredService<IProductSalesEligibilityV1>());
        Assert.IsType<UnacceptedProductFamilyRequirementPolicyV1>(scope.ServiceProvider.GetRequiredService<IProductFamilyRequirementPolicyV1>());
    }

    private static ServiceProvider Host(bool configured, bool settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:GoymenErp"] = "Server=127.0.0.1,1;Database=ProductModelOnly;Integrated Security=true;Encrypt=false",
        };
        if (configured)
        {
            values["ProductRuntime:IssuerLocalUserId"] = "5d0f7c2a-4b9e-4a53-8a2e-6c1d2f3a4b01";
            values["ProductRuntime:IssuerExternalIdentityId"] = "5d0f7c2a-4b9e-4a53-8a2e-6c1d2f3a4b02";
            values["ProductRuntime:CursorSigningKeyBase64"] = Convert.ToBase64String(new byte[32]);
        }

        var services = new ServiceCollection();
        var world = new ProductSalesEligibilityServiceTests.World();
        services.AddSingleton<ICompanyContext>(world.Company);
        services.AddSingleton<ICurrentUser>(world.User);
        if (settings) services.AddScoped<ISettingsPrerequisiteProvider, UnavailableSettingsPrerequisiteProvider>();
        services.AddProductRuntime(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestingEnvironment());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Product.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
