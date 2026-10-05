using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Purchasing.Infrastructure;

/// <summary>
/// Purchasing's module registration (PUR-4a): the DbContext, the store, the unit verifier, the purchasing
/// service and the supply reference port. The host calls it (the central writer's step) with the audit
/// interceptor through <paramref name="configureDbContext"/>, as for every module, and supplies the Cari
/// supplier account port (<see cref="IPurchasingPartyAccountPortV1"/>), the Settings provider and the central
/// authority. The Product purchase-eligibility port (<see cref="IProductPurchaseEligibilityPortV1"/>) is
/// optional: until the Product owner publishes it the service answers 503 fail-closed. The shell capability
/// provider is registered by the host once the Purchasing names are in the catalogue.
/// </summary>
public static class PurchasingModuleExtensions
{
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
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

        services.AddDbContext<PurchasingDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PurchasingStore>();
        services.AddScoped<IPurchasingStore>(serviceProvider => serviceProvider.GetRequiredService<PurchasingStore>());
        services.AddScoped<PurchaseUnitVerifier>();
        services.TryAddSingleton(new PurchasingServiceBindings());
        services.AddScoped<PurchasingService>(CreatePurchasingService);
        services.AddScoped<IPurchasingService>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseGoodsReceiptServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseGoodsReceiptQuantityComparisonServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseAcceptanceSourceV1, PurchaseAcceptanceSource>();
        services.AddScoped<IPurchaseSupplyReferencePortV1, PurchaseSupplyReferencePort>();
        // AT-025: the purchase section of the operations work list reads the approved lines awaiting receipt through this port only.
        services.AddScoped<IPurchaseOpenLineReadPortV1, PurchaseOpenLineReadPort>();
        return services;
    }

    // A named factory so that no registration statement of this module names the company context (architecture guard).
    private static PurchasingService CreatePurchasingService(IServiceProvider serviceProvider) => new PurchasingService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<IPurchasingPartyAccountPortV1>(),
        serviceProvider.GetService<IProductPurchaseEligibilityPortV1>(),
        serviceProvider.GetRequiredService<PurchaseUnitVerifier>(),
        serviceProvider.GetRequiredService<IPurchasingStore>(),
        serviceProvider.GetRequiredService<PurchasingServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptTransactionV1>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptReadsV1>());

    /// <summary>Applies Purchasing's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializePurchasingModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
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
