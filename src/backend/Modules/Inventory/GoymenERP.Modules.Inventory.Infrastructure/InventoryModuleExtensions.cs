using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Inventory's module registration. The API host does not call it yet: host wiring, permissions
/// and HTTP are INV-3, and the central writer's reviewed-inventory registration of this DbContext
/// and initializer comes with it. The audit interceptor is the host's to add through
/// <paramref name="configureDbContext"/>, as for every module.
/// </summary>
public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(
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

        services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<InventoryKilogramScale>();
        services.AddScoped<InventoryStore>();
        services.AddScoped<IInventoryLotStore>(serviceProvider => serviceProvider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryLabelStore>(serviceProvider => serviceProvider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryMovementStore>(serviceProvider => serviceProvider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryOpeningStore>(serviceProvider => serviceProvider.GetRequiredService<InventoryStore>());
        // KLN-D65: the physical allocation writer is the store behind the shared total cap, under the host's Sales line lock
        // (without the lock binder a create or a growing change is refused fail-closed, never capped without the lock).
        services.AddScoped<IInventoryPhysicalAllocationWriterV1>(serviceProvider =>
            new InventoryPhysicalAllocationDemandCapWriter(
                serviceProvider.GetRequiredService<InventoryStore>(),
                serviceProvider.GetRequiredService<InventoryDbContext>(),
                serviceProvider.GetRequiredService<IInventoryFutureDemandSourceV1>(),
                serviceProvider.GetRequiredService<IInventoryFutureUnitConverterV1>(),
                serviceProvider.GetService<ISalesOrderLineSerializationLockBinderV1>()));
        services.AddScoped<IInventoryGoodsReceiptSourceV1, InventoryGoodsReceiptSourceService>();
        // DEC-SAL-005: Inventory answers the Sales-declared active-allocation read over its own company-filtered allocations.
        // Registered here, in the owner's module (no host file), so that Sales reads allocations only through this port.
        services.AddScoped<ISalesOrderAllocationReadPortV1, InventorySalesOrderAllocationReadPort>();
        // F-017 STOK §5.1 (STK-S16): the same read with its kind — physical (kg) and future supply (purchase unit) kept apart.
        // V1 above stays registered and unchanged for every physical-only caller.
        services.AddScoped<ISalesOrderAllocationReadPortV2, InventorySalesOrderAllocationReadPortV2>();
        // SAT-D262: Inventory owns the moving-average purchase cost and answers Sales only through this versioned read port.
        services.AddScoped<IInventoryAverageCostReadPortV1, InventoryAverageCostReadPort>();
        // SAT-D272 / SAT-D347 / SAT-D350 (STK-S35): the company-wide (all locations) average; the aggregation is Inventory's.
        services.AddScoped<IInventoryCompanyWideAverageCostReadPortV1, InventoryCompanyWideAverageCostReadPort>();
        // KLN-D85 (STK-SC33-a): the same figure in another unit, over the Settings unit conversion policy. Without the Settings
        // ports or a kilograms-per-unit adapter (the host's, over the Product owner's answer) the conversion is refused.
        services.TryAddScoped<IInventoryKilogramsPerUnitSourceV1, NoSourceInventoryKilogramsPerUnitSourceV1>();
        services.AddScoped<IInventoryAverageCostUnitReadPortV1>(serviceProvider => new InventoryAverageCostUnitReadPort(
            serviceProvider.GetRequiredService<IInventoryCompanyWideAverageCostReadPortV1>(),
            serviceProvider.GetService<ISettingsUnitIdentityCatalogV1>(),
            serviceProvider.GetService<ISettingsUnitConversionPolicyProviderV1>(),
            serviceProvider.GetRequiredService<IInventoryKilogramsPerUnitSourceV1>()));
        // STK-S35: the ledger's only writer. No movement path calls it until the purchase price input has a port (SAT-D295).
        services.AddScoped<InventoryAverageCostLedgerWriter>();
        services.AddScoped<IInventoryManufacturingPlanAllocationReadPortV1, InventoryManufacturingPlanAllocationReadPort>();
        services.AddScoped<IInventoryLabelDispositionReadPortV1, InventoryLabelDispositionReadPort>();
        services.AddScoped<IInventoryFirstReceiptExchangeRateReadPortV1, InventoryFirstReceiptExchangeRateReadPort>();
        services.AddScoped<IInventoryFreeQuantityReadPortV1, InventoryFreeQuantityReadPort>();
        // STK-S24: per-product physical kg for the Logistics weighing-difference record (read only; Api host adapter).
        services.AddScoped<IInventoryProductPhysicalStockReadPortV1, InventoryProductPhysicalStockReadPort>();
        services.TryAddScoped<IInventoryLabelGenealogyReaderV1>(serviceProvider =>
            serviceProvider.GetRequiredService<InventoryStore>());
        // INV-OPEN-1 part 2: the opening-count command; its location and product ports are the host's adapters.
        services.AddScoped<IInventoryOpeningCommandService, InventoryOpeningCommandService>();
        services.AddInventoryStockPositions();
        services.AddInventoryLabelDetails();
        services.AddInventoryMovementTimeline();
        // KLN-D42 (STK-S31): the dated shortage warning read; the purchase source is the host's adapter.
        services.AddInventoryShortageWarnings();
        // F-017 STOK F017-b: the future supply allocation writer (no route yet, F017-b2) and the ports Purchasing and Sales use.
        // The purchase and sales line lock binders and the purchase line source are the host's; the defaults below are
        // fail-closed (identity-only conversion, purchase source unavailable) until the host replaces them.
        services.AddScoped<IInventoryFutureAllocationWriterV1, InventoryFutureAllocationWriter>();
        services.AddScoped<IInventoryFutureAllocationReadPortV1, InventoryFutureAllocationReadPort>();
        services.AddScoped<IInventoryFutureAllocationReleasePortV1, InventoryFutureAllocationReleasePort>();
        services.AddScoped<IInventoryFutureDemandSourceV1, InventoryFutureDemandSource>();
        // KLN-D66: over the Settings unit identities when the host has them (kg only today), identity-only otherwise.
        services.TryAddScoped<IInventoryFutureUnitConverterV1>(serviceProvider =>
            serviceProvider.GetService<ISettingsUnitIdentityCatalogV1>() is { } units
                ? (IInventoryFutureUnitConverterV1)new InventorySettingsFutureUnitConverterV1(units)
                : new InventoryIdentityFutureUnitConverterV1());
        // F-017 STOK F017-c: the goods receipt host enlists this port in its own transaction (ALS-D84 order).
        services.AddScoped<IInventoryGoodsReceiptFutureSupplyPortV1, InventoryGoodsReceiptFutureSupplyPort>();
        services.TryAddScoped<IInventoryFutureSupplySourceV1, InventoryUnavailableFutureSupplySourceV1>();
        // KLN-D34/D39/D40/D70 (STK-S30): label suggestions and the substitute ("muadil") check. The Settings tolerance,
        // the Product similarity facts and the AI tool are other owners' answers; the defaults are fail-closed (no
        // tolerance: the same product only; facts unavailable; AI disabled while no provider is configured).
        services.TryAddScoped<IInventorySimilarProductToleranceSourceV1, UnconfiguredInventorySimilarProductToleranceSourceV1>();
        services.TryAddScoped<IInventoryProductSimilarityFactsV1, UnavailableInventoryProductSimilarityFactsV1>();
        services.TryAddSingleton<IInventoryAllocationAiSuggestionToolV1, DisabledInventoryAllocationAiSuggestionToolV1>();
        services.AddScoped<IInventorySubstituteProductVerifierV1, InventorySubstituteProductVerifier>();
        services.AddScoped<IInventoryAllocationSuggestionReaderV1, InventoryAllocationSuggestionReader>();
        services.AddSingleton<IShellCapabilityProvider, InventoryShellCapabilityProvider>();
        return services;
    }

    /// <summary>Applies Inventory's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeInventoryModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

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
