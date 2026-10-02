using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ProductPriceListHostCompositionTests
{
    [Fact]
    public async Task Disabled_product_runtime_registers_only_the_fail_closed_service_and_never_database_readers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions("unused", null, null));
        services.AddProductPriceListHost(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProductPriceListServiceV1>();
        var exception = await Assert.ThrowsAsync<ProductPriceListException>(() => service.ListAsync(null, null));
        Assert.Equal((StatusCodes.Status503ServiceUnavailable, ProductPriceListCodesV1.Unavailable),
            (exception.Status, exception.Code));
    }

    [Fact]
    public void Enabled_product_runtime_keeps_the_owner_service_readers_and_database_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions(
            "unused",
            new ProductPilotIssuerBinding(Guid.NewGuid(), Guid.NewGuid()),
            new byte[32]));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] = "Server=localhost;Database=GoymenERP_CompositionOnly;Integrated Security=true;TrustServerCertificate=true",
            })
            .Build();

        services.AddProductPriceListHost(configuration, (_, options) => options.EnableDetailedErrors());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));
    }

    [Fact]
    public void Final_test_runtime_override_recomposes_price_lists_once_without_weakening_disabled_hosts()
    {
        const string connection =
            "Server=localhost;Database=GoymenERP_CompositionOnly;Integrated Security=true;TrustServerCertificate=true";
        var services = new ServiceCollection();
        services.AddSingleton(new ProductRuntimeOptions("unused", null, null));
        services.AddProductPriceListHost(new ConfigurationBuilder().Build());
        var failClosedDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));

        services.RemoveAll<ProductRuntimeOptions>();
        services.AddSingleton(new ProductRuntimeOptions(
            connection,
            new ProductPilotIssuerBinding(Guid.NewGuid(), Guid.NewGuid()),
            new byte[32]));

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));

        SqlServerRelationalApiFactory.EnsureFinalProductPriceListHostComposition(services, connection);
        AssertFinalComposition(services, failClosedDescriptor);

        SqlServerRelationalApiFactory.EnsureFinalProductPriceListHostComposition(services, connection);
        AssertFinalComposition(services, failClosedDescriptor);

        static void AssertFinalComposition(
            IServiceCollection descriptors,
            ServiceDescriptor failClosedDescriptor)
        {
            Assert.DoesNotContain(failClosedDescriptor, descriptors);
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(ProductPriceListDbContext));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductPriceListServiceV1));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductPriceReferenceReaderV1));
            Assert.Single(descriptors, descriptor => descriptor.ServiceType == typeof(IProductQuantityPriceReferenceReaderV1));
        }
    }
}
