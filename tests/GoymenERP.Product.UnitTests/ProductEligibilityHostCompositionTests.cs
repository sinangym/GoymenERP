using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// C3: the host composition of the Product owner's sales and stock eligibility runtimes, as the
/// owner decided it (the accepted GDC band/coil rule set over the shipped unavailable definition
/// source). A composition test, not SQL or HTTP evidence.
/// </summary>
public sealed class ProductEligibilityHostCompositionTests
{
    private static readonly ProductStockSelectionV1 StockSelection = new(
        ProductSalesEligibilityServiceTests.ProductId, ProductSalesEligibilityServiceTests.Version,
        ProductSalesEligibilityServiceTests.On);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Without_the_runtime_or_a_Settings_provider_both_ports_are_unavailable(bool configured, bool settings)
    {
        await using var provider = Host(configured, settings);
        await using var scope = provider.CreateAsyncScope();

        var sales = await Assert.ThrowsAsync<ProductOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IProductSalesEligibilityV1>()
            .EvaluateAsync(ProductSalesEligibilityServiceTests.Selection, default));
        var stock = await Assert.ThrowsAsync<ProductOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IProductStockEligibilityV1>()
            .EvaluateAsync(StockSelection, default));

        Assert.Equal((503, "product_runtime_unconfigured"), (sales.StatusCode, sales.Code));
        Assert.Equal((503, "product_runtime_unconfigured"), (stock.StatusCode, stock.Code));
    }

    [Fact]
    public async Task The_configured_host_composes_the_owner_runtimes_with_the_accepted_family_policy()
    {
        await using var provider = Host(configured: true, settings: true);
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ProductSalesEligibilityService>(scope.ServiceProvider.GetRequiredService<IProductSalesEligibilityV1>());
        Assert.IsType<ProductStockEligibilityService>(scope.ServiceProvider.GetRequiredService<IProductStockEligibilityV1>());
        Assert.IsType<GdcBandCoilFamilyRequirementPolicyV1>(
            scope.ServiceProvider.GetRequiredService<IProductFamilyRequirementPolicyV1>());
        // Rules accepted, data not: the definition source is the shipped unavailable one.
        Assert.IsType<UnavailableGdcBandCoilFamilyDefinitionSourceV1>(
            scope.ServiceProvider.GetRequiredService<IGdcBandCoilFamilyDefinitionSourceV1>());
        Assert.Single(scope.ServiceProvider.GetServices<IProductSalesEligibilityV1>());
        Assert.Single(scope.ServiceProvider.GetServices<IProductStockEligibilityV1>());
    }

    [Fact]
    public async Task The_composition_registers_no_Settings_provider_of_its_own()
    {
        await using var provider = Host(configured: true, settings: false);

        Assert.Null(provider.GetService<ISettingsPrerequisiteProvider>());
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
        services.AddSingleton<IErpAuthorityRevalidationService>(world.Authorization);
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
