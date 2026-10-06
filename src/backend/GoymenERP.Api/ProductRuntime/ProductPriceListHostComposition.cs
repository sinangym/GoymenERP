using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// Composes the Product-owned price-list implementation only when the existing Product runtime is enabled.
/// A disabled host keeps the public routes fail-closed without registering database-backed readers.
/// </summary>
public static class ProductPriceListHostComposition
{
    public static IServiceCollection AddProductPriceListHost(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var runtime = services
            .Where(descriptor => descriptor.ServiceType == typeof(ProductRuntimeOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProductRuntimeOptions>()
            .LastOrDefault()
            ?? throw new InvalidOperationException("Product runtime options must be registered before price-list composition.");

        if (runtime.IsEnabled)
        {
            return services.AddProductPriceLists(configuration, configureDbContext);
        }

        services.TryAddScoped<IProductPriceListServiceV1, UnavailableProductPriceListService>();
        return services;
    }
}

internal sealed class UnavailableProductPriceListService : IProductPriceListServiceV1
{
    public Task<PriceListPageV1> ListAsync(int? pageSize, string? cursor, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListPageV1>(cancellationToken);

    public Task<PriceListViewV1> GetAsync(Guid priceListId, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListViewV1>(cancellationToken);

    public Task<PriceListProductOptionsV1> GetProductOptionsAsync(string? query, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListProductOptionsV1>(cancellationToken);

    public Task<PriceListResultV1> CreateAsync(CreatePriceListRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default) => Unavailable<PriceListResultV1>(cancellationToken);

    public Task<PriceListResultV1> ReviseDraftAsync(Guid priceListId, RevisePriceListDraftRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListResultV1>(cancellationToken);

    public Task<PriceListResultV1> StartVersionAsync(Guid priceListId, StartPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListResultV1>(cancellationToken);

    public Task<PriceListResultV1> PublishAsync(Guid priceListId, PublishPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Unavailable<PriceListResultV1>(cancellationToken);

    private static Task<T> Unavailable<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<T>(new ProductPriceListException(
            StatusCodes.Status503ServiceUnavailable,
            ProductPriceListCodesV1.Unavailable));
    }
}
