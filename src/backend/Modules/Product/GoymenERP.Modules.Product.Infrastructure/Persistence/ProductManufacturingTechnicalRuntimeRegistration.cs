using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product-owned composition seam. The central API host supplies only its existing Product runtime
/// enablement predicate; this registration adds no route, permission, grant, fixture or migration.
/// </summary>
public static class ProductManufacturingTechnicalRuntimeRegistration
{
    public static IServiceCollection AddProductManufacturingTechnicalSnapshot(this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.TryAddScoped<IProductManufacturingTechnicalRecordReader>(
            provider => provider.GetRequiredService<ProductReadStore>());
        services.AddScoped<IProductManufacturingTechnicalSnapshotV1>(provider =>
            runtimeEnabled(provider)
            && provider.GetService<ISettingsPrerequisiteProvider>() is not null
            && provider.GetService<IProductFamilyDefinitionSourceV2>() is not null
                ? ActivatorUtilities.CreateInstance<ProductManufacturingTechnicalSnapshotService>(provider)
                : new UnavailableProductManufacturingTechnicalSnapshot());
        services.AddScoped<IProductManufacturingTechnicalSnapshotV2>(provider =>
            runtimeEnabled(provider)
            && provider.GetService<ISettingsPrerequisiteProvider>() is not null
            && provider.GetService<IProductFamilyDefinitionSourceV2>() is not null
                ? ActivatorUtilities.CreateInstance<ProductManufacturingTechnicalSnapshotService>(provider)
                : new UnavailableProductManufacturingTechnicalSnapshot());
        return services;
    }
}

internal sealed class UnavailableProductManufacturingTechnicalSnapshot :
    IProductManufacturingTechnicalSnapshotV1,
    IProductManufacturingTechnicalSnapshotV2
{
    public Task<ProductManufacturingTechnicalSnapshotResultV1> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductManufacturingTechnicalSnapshotResultV1>(
            new ProductOperationException("product_runtime_unconfigured", 503));
    }

    public Task<ProductManufacturingTechnicalSnapshotResultV2> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV2 selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductManufacturingTechnicalSnapshotResultV2>(
            new ProductOperationException("product_runtime_unconfigured", 503));
    }
}
