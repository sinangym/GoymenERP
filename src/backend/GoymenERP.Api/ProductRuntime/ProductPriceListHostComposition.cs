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
            // MOD-02: a customer-specific list asks the Cari owner through this adapter (registered before the service resolves it).
            services.TryAddScoped<IProductPriceListCustomerPortV1, CounterpartyProductPriceListCustomerPortV1>();
            // SAT-D382: a package standard override's customer is asked of the Cari owner through this adapter.
            services.TryAddScoped<IProductPackageStandardCustomerPortV3, CounterpartyProductPackageStandardCustomerPortV3>();
            return services.AddProductPriceLists(configuration, configureDbContext);
        }

        services.TryAddScoped<IProductPriceListServiceV1, UnavailableProductPriceListService>();
        // SAT-D134: the product item type lives in the same company-scoped context, so it is fail-closed here too (the routes
        // answer 503 and the Settings line tax port reads "unavailable").
        services.TryAddScoped<IProductItemTypeServiceV1, UnavailableProductItemTypeService>();
        services.TryAddScoped<IProductItemTypeReaderV1, UnavailableProductItemTypeService>();
        // BS-9: the "Ebat/kalınlık" row lives in the same context: the routes answer 503 and the read port "unavailable".
        services.TryAddScoped<IProductDimensionServiceV1, UnavailableProductDimensionService>();
        services.TryAddScoped<IProductDimensionReaderV1, UnavailableProductDimensionService>();
        // URN-D16: the kg/m table lives in the same context: its routes answer 503.
        services.TryAddScoped<IProductProfileWeightTableServiceV1, UnavailableProductProfileWeightTableService>();
        // B-08 / BS-10: the package standard table lives in the same context: its routes answer 503 and the read port
        // "unavailable" (never NoSource, which would claim there is no table).
        services.TryAddScoped<IProductPackageStandardServiceV2, UnavailableProductPackageStandardService>();
        services.TryAddScoped<IProductPackageStandardReaderV2, UnavailableProductPackageStandardService>();
        services.TryAddScoped<IProductPackageStandardServiceV3, UnavailableProductPackageStandardService>();
        services.TryAddScoped<IProductPackageStandardReaderV3, UnavailableProductPackageStandardService>();
        return services;
    }
}

internal sealed class UnavailableProductItemTypeService : IProductItemTypeServiceV1, IProductItemTypeReaderV1
{
    public Task<ProductItemTypeViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductItemTypeViewV1>(Unavailable(cancellationToken));

    public Task<ProductItemTypeResultV1> SetAsync(Guid productId, SetProductItemTypeRequestV1 request,
        ProductItemTypeCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductItemTypeResultV1>(Unavailable(cancellationToken));

    Task<ProductItemTypeReadV1> IProductItemTypeReaderV1.GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Unavailable, null, null));
    }

    private static ProductItemTypeException Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductItemTypeException(StatusCodes.Status503ServiceUnavailable, ProductItemTypeProblemCodesV1.ProviderUnavailable);
    }
}

internal sealed class UnavailableProductDimensionService : IProductDimensionServiceV1, IProductDimensionReaderV1
{
    public Task<ProductDimensionViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductDimensionViewV1>(Unavailable(cancellationToken));

    public Task<ProductDimensionResultV1> SetAsync(Guid productId, SetProductDimensionRequestV1 request,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductDimensionResultV1>(Unavailable(cancellationToken));

    Task<ProductDimensionReadV1> IProductDimensionReaderV1.GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Unavailable));
    }

    private static ProductDimensionException Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductDimensionException(StatusCodes.Status503ServiceUnavailable,
            ProductDimensionProblemCodesV1.ProviderUnavailable);
    }
}

internal sealed class UnavailableProductProfileWeightTableService : IProductProfileWeightTableServiceV1
{
    public Task<ProductProfileWeightTableViewV1> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ProductProfileWeightTableViewV1>(Unavailable(cancellationToken));

    public Task<ProductProfileWeightResultV1> ImportAsync(ImportProductProfileWeightTableRequestV1 request,
        ProductProfileWeightCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductProfileWeightResultV1>(Unavailable(cancellationToken));

    private static ProductProfileWeightException Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductProfileWeightException(StatusCodes.Status503ServiceUnavailable,
            ProductProfileWeightProblemCodesV1.ProviderUnavailable);
    }
}

internal sealed class UnavailableProductPackageStandardService : IProductPackageStandardServiceV2, IProductPackageStandardReaderV2,
    IProductPackageStandardServiceV3, IProductPackageStandardReaderV3
{
    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardApprovalViewV3>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateForApproverAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardApprovalViewV3>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardImportFileListV3> ListImportsAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardImportFileListV3>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardImportReportV3?> GetImportReportAsync(Guid importId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardImportReportV3?>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardResultV2> SetCustomerAsync(Guid customerId, Guid productId,
        SetProductPackageStandardRequestV2 request, ProductPackageStandardCommandMetadataV2 metadata,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardResultV2>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardResultV2> ApproveAsync(long version, ApproveProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardResultV2>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardResultV2> RejectAsync(long version, RejectProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardResultV2>(Unavailable(cancellationToken));

    Task<ProductPackageStandardReadV3> IProductPackageStandardReaderV3.GetAsync(Guid productId, Guid? customerId, DateOnly on,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductPackageStandardReadV3.Without(ProductPackageStandardReadOutcomeV2.Unavailable));
    }

    public Task<ProductPackageStandardTableViewV2> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardTableViewV2>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardResultV2> ImportAsync(ImportProductPackageStandardsRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardResultV2>(Unavailable(cancellationToken));

    public Task<ProductPackageStandardResultV2> SetAsync(Guid productId, SetProductPackageStandardRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default) =>
        Task.FromException<ProductPackageStandardResultV2>(Unavailable(cancellationToken));

    Task<ProductPackageStandardReadV2> IProductPackageStandardReaderV2.GetAsync(Guid productId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.Unavailable));
    }

    private static ProductPackageStandardException Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductPackageStandardException(StatusCodes.Status503ServiceUnavailable,
            ProductPackageStandardProblemCodesV2.ProviderUnavailable);
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
