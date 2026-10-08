using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Sales.Infrastructure;

/// <summary>
/// Sales's module registration: the DbContext, the store and the draft service of the API slice.
/// The host calls it (the central writer's step) with the audit interceptor through
/// <paramref name="configureDbContext"/>, as for every module, and supplies the Cari account port,
/// the Settings provider, the optional Product eligibility port and the central authority. The
/// shell capability provider is registered by the host once the Sales names are in the catalogue.
/// </summary>
public static class SalesModuleExtensions
{
    public static IServiceCollection AddSalesModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<SalesDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SalesStore>();
        services.AddScoped<ISalesOrderDraftStore>(serviceProvider => serviceProvider.GetRequiredService<SalesStore>());
        // The draft use cases (Sales API slice). The host supplies the Cari account port, the Settings
        // provider, the optional Product eligibility port and the central authority; the shell
        // capability provider is registered by the host once the Sales names are in the catalogue.
        services.TryAddSingleton(new SalesDraftServiceBindings());
        services.AddScoped<ISalesDraftService>(CreateDraftService);
        // SALES-A1: the Sales-owned line reference port (Business 5737989917 = B) for the allocation
        // and loading consumers, over the same company-filtered store. Registered here, in the module
        // (no host file), so that a consumer module reads Sales only through this port.
        services.AddScoped<ISalesOrderLineReferencePortV1, SalesOrderLineReferencePort>();
        return services;
    }

    // Built in a named factory so that no registration statement of this module names the company
    // context: the host's scoped ICompanyContext is resolved here, never registered (architecture guard).
    private static SalesDraftService CreateDraftService(IServiceProvider serviceProvider) => new SalesDraftService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<IActiveRoleCodeReader>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<ISalesPartyAccountPortV1>(),
        serviceProvider.GetRequiredService<ISalesPartyDisplaySourceV1>(),
        serviceProvider.GetRequiredService<ISettingsPrerequisiteProvider>(),
        serviceProvider.GetService<IProductSalesEligibilityV1>(),
        serviceProvider.GetRequiredService<ISalesOrderDraftStore>(),
        serviceProvider.GetRequiredService<SalesDraftServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        // SALES-A2a: the Cari contact-information port is the host's registration (central); until it is
        // registered a draft naming a delivery address or contact is 503, never stored unvalidated.
        serviceProvider.GetService<ISalesPartyContactInformationPortV1>(),
        // AT-024: the delivery owner's read port, registered by that owner's module (Logistics); until it is, the
        // delivered / remaining read is 503, never an assumed zero.
        serviceProvider.GetService<ISalesOrderDeliveryReadPortV1>(),
        // DEC-SAL-005: the impact owners' read ports, registered by those owners' modules (Inventory: active allocations;
        // Logistics: loading-instruction lines); until one is, its category of the cancellation preview is unreadable and a
        // cancellation is refused, never decided on an assumed zero.
        serviceProvider.GetService<ISalesOrderAllocationReadPortV1>(),
        serviceProvider.GetService<ISalesOrderLoadingReadPortV1>(),
        // AT-018 / AT-021: the Product price-list reference read, registered by the Product owner's AddProductPriceLists;
        // until it is, a list-price reference is 503, never an assumed "no price".
        serviceProvider.GetService<IProductQuantityPriceReferenceReaderV1>());

    /// <summary>Applies Sales's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeSalesModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
