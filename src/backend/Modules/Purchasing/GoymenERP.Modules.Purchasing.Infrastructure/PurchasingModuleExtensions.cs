using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
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
        services.AddScoped<IPurchasingOrderRecordReadSourceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchasingOrderReadServiceV1, PurchasingOrderReadServiceV1>();
        services.AddScoped<IPurchaseGoodsReceiptServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseGoodsReceiptQuantityComparisonServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        // ALC-S16 (F-027): the bulk goods receipt (KLN-D41/D43/D45).
        services.AddScoped<IPurchaseGoodsReceiptBatchStore, PurchaseGoodsReceiptBatchStore>();
        services.AddScoped<IPurchaseGoodsReceiptBatchServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseAcceptanceSourceV1, PurchaseAcceptanceSource>();
        services.AddScoped<IPurchaseSupplyReferencePortV1, PurchaseSupplyReferencePort>();
        // AT-025: the purchase section of the operations work list reads the approved lines awaiting receipt through this port only.
        services.AddScoped<IPurchaseOpenLineReadPortV1, PurchaseOpenLineReadPort>();
        // A4 / MOD-03: the open purchase lines serving a Sales order (the host adapts it to Sales's linked-supply read contract).
        services.AddScoped<IPurchaseSalesLinkedSupplyReadPortV1, PurchaseSalesLinkedSupplyReadPort>();
        // F-017 F17-1 (ALS-D82): the allocatable amount of an approved general line, read by Inventory's future allocation only.
        services.AddScoped<IPurchaseFutureSupplyReadPortV1, PurchaseFutureSupplyReadPort>();
        // BS-1 (F-011): the invoiced exchange rate of one goods receipt line, read by Inventory's average cost (Unavailable until invoiced).
        services.AddScoped<IPurchaseInvoiceCostRateReadPortV1, PurchaseInvoiceCostRateReadPort>();
        // SAT-D295 (BS-1): the recorded price of one purchase order line, read by Inventory's average cost (the rate is the invoice's).
        services.AddScoped<IPurchaseOrderLineCostReadPortV1, PurchaseOrderLineCostReadPort>();
        // FNS-D73: the exchange rate specified on one purchase order (none recorded yet: answered fail-closed, NEEDS-SINAN).
        services.AddScoped<IPurchaseOrderExchangeRateReadPortV1, PurchaseOrderExchangeRateReadPort>();
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
        serviceProvider.GetService<IPurchaseGoodsReceiptReadsV1>(),
        // A2a-1 (TEK-01): the existing Settings money port and its operation-aware candidate read (ALS-D58: no new Settings port).
        serviceProvider.GetService<ISettingsPrerequisiteProvider>(),
        serviceProvider.GetService<ISettingsMoneyCandidateReaderV2>(),
        // A2a-2 (API-20, ALS-D74): the existing Settings line tax port for the item type's default VAT (no new Settings port).
        serviceProvider.GetService<ISettingsLineTaxResolverV1>(),
        // A4b (SAT-D220): Sales's approved line read, adapted by the host (not registered: Sales-sourced new lines stay closed).
        serviceProvider.GetService<IPurchaseRequestSalesSourcePortV1>(),
        // ALC-S16 (F-027): the bulk receipt's store and host transaction; the Ayarlar policy and Inventory's label register are optional
        // (unwired: no tolerance = every kg difference waits for approval, no generated code and no suffix suggestion — KLN-D41/D45).
        serviceProvider.GetRequiredService<IPurchaseGoodsReceiptBatchStore>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptBatchTransactionV1>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptPolicyReadPortV1>(),
        serviceProvider.GetService<IPurchaseLabelCodeRegisterReadPortV1>());

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
