using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>Product-owned DI seam; route, Inventory composition and grants remain central-owned.</summary>
public static class ProductStockDisplayRuntimeRegistration
{
    public static IServiceCollection AddProductStockDisplayReferences(
        this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.TryAddScoped<IProductStockDisplayRecordReader>(
            provider => provider.GetRequiredService<ProductReadStore>());
        services.AddScoped<ProductStockDisplayReferenceService>();
        services.AddScoped<IProductStockDisplayReferencesV1>(provider =>
            runtimeEnabled(provider) && provider.GetService<IProductFamilyDefinitionSourceV2>() is not null
                ? provider.GetRequiredService<ProductStockDisplayReferenceService>()
                : new UnavailableProductStockDisplayReferences());
        services.AddScoped<IProductStockTechnicalMatcherV1>(provider =>
            runtimeEnabled(provider) && provider.GetService<IProductFamilyDefinitionSourceV2>() is not null
                ? provider.GetRequiredService<ProductStockDisplayReferenceService>()
                : new UnavailableProductStockDisplayReferences());
        services.AddScoped<IProductStockFamilyOptionsV1>(provider =>
            runtimeEnabled(provider) && provider.GetService<IProductFamilyDefinitionSourceV2>() is not null
                ? provider.GetRequiredService<ProductStockDisplayReferenceService>()
                : new UnavailableProductStockDisplayReferences());
        return services;
    }
}

internal sealed class UnavailableProductStockDisplayReferences
    : IProductStockDisplayReferencesV1, IProductStockTechnicalMatcherV1, IProductStockFamilyOptionsV1
{
    public Task<ProductStockFamilyOptionsV1> ReadFamilyOptionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductStockFamilyOptionsV1>(
            new ProductOperationException("product_runtime_unconfigured", 503));
    }

    public Task<ProductStockDisplayBatchV1> ReadAsync(
        ProductStockDisplayBatchQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductStockDisplayBatchV1>(
            new ProductOperationException("product_runtime_unconfigured", 503));
    }

    public Task<ProductStockTechnicalMatchBatchV1> MatchAsync(
        ProductStockTechnicalMatchQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductStockTechnicalMatchBatchV1>(
            new ProductOperationException("product_runtime_unconfigured", 503));
    }
}
