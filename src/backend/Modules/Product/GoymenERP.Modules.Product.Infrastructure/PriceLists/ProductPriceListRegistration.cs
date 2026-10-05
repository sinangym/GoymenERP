using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// AT-018 owner registration seam. The central host calls <see cref="AddProductPriceLists"/> with the audit interceptor
/// through <paramref name="configureDbContext"/> (as for every company-scoped module), maps the endpoints and runs
/// <see cref="InitializeProductPriceListsAsync"/> after Organization and the Product card tables (the foreign keys
/// reference both). This extension adds no route, permission, grant or shell capability.
/// </summary>
public static class ProductPriceListRegistration
{
    public static IServiceCollection AddProductPriceLists(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<ProductPriceListDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(
                ProductPriceListDbContext.MigrationsHistoryTable, ProductPriceListDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped(CreateStore);
        services.AddScoped<IProductPriceListServiceV1>(CreateService);
        services.AddScoped<IProductPriceReferenceReaderV1>(CreateReader);
        services.AddScoped<IProductQuantityPriceReferenceReaderV1>(CreateReader);
        return services;
    }

    /// <summary>Applies the price-list migrations. Its foreign keys need Organization's companies and Product's cards first.</summary>
    public static async Task InitializeProductPriceListsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProductPriceListDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    // Named factories so that no registration statement of this module names the company context (architecture guard).
    private static ProductPriceListStore CreateStore(IServiceProvider serviceProvider) => new(
        serviceProvider.GetRequiredService<ProductPriceListDbContext>(),
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<IAuditEventCollector>(),
        serviceProvider.GetRequiredService<TimeProvider>());

    private static ProductPriceListService CreateService(IServiceProvider serviceProvider) => new(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ProductPriceListStore>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<ISettingsUnitIdentityCatalogV1>(),
        ErpPermissions.IsKnown);

    private static ProductPriceReferenceReader CreateReader(IServiceProvider serviceProvider) => new(
        serviceProvider.GetRequiredService<ProductPriceListStore>(),
        serviceProvider.GetRequiredService<ICompanyContext>());
}
