using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public static class ProductPurchaseUnitConversionRuntimeRegistration
{
    public static IServiceCollection AddProductPurchaseUnitConversion(this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.TryAddScoped<SqlProductPurchaseReader>();
        services.TryAddScoped<IProductPurchaseRecordReader>(provider => provider.GetRequiredService<SqlProductPurchaseReader>());
        services.TryAddScoped<ProductUnitProfileAdapter>();
        services.AddScoped<IProductPurchaseUnitConversionV1>(provider => runtimeEnabled(provider)
            && provider.GetService<IProductDimensionReaderV1>() is not null
            && provider.GetService<ISettingsPhysicalUnitSemanticsV1>() is not null
            && provider.GetService<ISettingsUnitConversionPolicyProviderV1>() is not null
            && provider.GetService<ISettingsPrerequisiteProvider>() is not null
                ? new ProductPurchaseUnitConversionService(
                    provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(),
                    provider.GetRequiredService<IProductPurchaseRecordReader>(), provider.GetRequiredService<IProductDimensionReaderV1>(),
                    provider.GetRequiredService<ISettingsPhysicalUnitSemanticsV1>(), provider.GetRequiredService<ProductUnitProfileAdapter>(),
                    provider.GetRequiredService<ISettingsUnitConversionPolicyProviderV1>())
                : new UnavailableProductPurchaseUnitConversion());
        return services;
    }

    private sealed class UnavailableProductPurchaseUnitConversion : IProductPurchaseUnitConversionV1
    {
        public Task<ProductPurchaseUnitConversionReadV1> ReadAsync(Guid productId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductPurchaseUnitConversionReadV1(ProductPurchaseUnitConversionOutcomeV1.Unavailable, null));
        }
        public Task<ProductPurchaseUnitConversionResultV1> ConvertAsync(ProductPurchaseUnitConversionRequestV1 request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductPurchaseUnitConversionResultV1(ProductPurchaseUnitConversionOutcomeV1.Unavailable, null, null, null, null));
        }
        public Task<ProductPurchaseWeighbridgeResultV1> CheckWeighbridgeAsync(ProductPurchaseWeighbridgeRequestV1 request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductPurchaseWeighbridgeResultV1(ProductPurchaseUnitConversionOutcomeV1.Unavailable, null, null, null));
        }
    }
}
