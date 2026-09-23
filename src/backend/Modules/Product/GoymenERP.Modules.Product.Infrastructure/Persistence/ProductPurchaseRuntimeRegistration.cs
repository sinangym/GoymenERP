using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>Product-owned composition; the central host supplies its existing runtime enablement predicate.
/// Merely calling this registers no permission, fixture, grant, migration or endpoint.</summary>
public static class ProductPurchaseRuntimeRegistration
{
    public static IServiceCollection AddProductPurchaseSelection(this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.AddScoped<IProductPurchaseFamilyRequirementPolicyV1, ProductPurchaseFamilyRequirementPolicyV1>();
        services.AddScoped<SqlProductPurchaseReader>();
        services.AddScoped<IProductPurchaseRecordReader>(p => p.GetRequiredService<SqlProductPurchaseReader>());
        services.AddScoped<IProductPurchaseCandidateReader>(p => p.GetRequiredService<SqlProductPurchaseReader>());
        services.AddScoped<ProductPurchaseEligibilityService>();
        services.AddScoped<ProductPurchaseCandidateService>();
        services.AddScoped<IProductPurchaseEligibilityV1>(p => Available(p)
            ? p.GetRequiredService<ProductPurchaseEligibilityService>() : new UnavailableProductPurchaseSelection());
        services.AddScoped<IProductPurchaseCandidatesV1>(p => Available(p)
            ? p.GetRequiredService<ProductPurchaseCandidateService>() : new UnavailableProductPurchaseSelection());
        return services;

        bool Available(IServiceProvider p) => runtimeEnabled(p) && p.GetService<ISettingsPrerequisiteProvider>() is not null
            && p.GetService<IProductFamilyDefinitionSourceV2>() is not null;
    }
}

internal sealed class UnavailableProductPurchaseSelection : IProductPurchaseEligibilityV1, IProductPurchaseCandidatesV1
{
    public Task<ProductPurchaseEligibilityV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductPurchaseEligibilityV1>(new ProductOperationException("product_runtime_unconfigured", 503));
    }
    public Task<ProductPurchaseCandidatesV1> SearchAsync(ProductPurchaseCandidateQueryV1 query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ProductPurchaseCandidatesV1>(new ProductOperationException("product_runtime_unconfigured", 503));
    }
}
