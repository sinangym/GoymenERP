using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
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
        services.AddScoped<IInventoryPhysicalAllocationWriterV1>(serviceProvider =>
            serviceProvider.GetRequiredService<InventoryStore>());
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
        // STK-S35: the ledger's only writer. No movement path calls it until the purchase price input has a port (SAT-D295).
        services.AddScoped<InventoryAverageCostLedgerWriter>();
        services.AddScoped<IInventoryManufacturingPlanAllocationReadPortV1, InventoryManufacturingPlanAllocationReadPort>();
        services.AddScoped<IInventoryLabelDispositionReadPortV1, InventoryLabelDispositionReadPort>();
        services.AddScoped<IInventoryFreeQuantityReadPortV1, InventoryFreeQuantityReadPort>();
        services.TryAddScoped<IInventoryLabelGenealogyReaderV1>(serviceProvider =>
            serviceProvider.GetRequiredService<InventoryStore>());
        // INV-OPEN-1 part 2: the opening-count command; its location and product ports are the host's adapters.
        services.AddScoped<IInventoryOpeningCommandService, InventoryOpeningCommandService>();
        services.AddInventoryStockPositions();
        services.AddInventoryLabelDetails();
        services.AddInventoryMovementTimeline();
        // F-017 STOK F017-b: the future supply allocation writer (no route yet, F017-b2) and the ports Purchasing and Sales use.
        // The purchase and sales line lock binders and the purchase line source are the host's; the defaults below are
        // fail-closed (identity-only conversion, purchase source unavailable) until the host replaces them.
        services.AddScoped<IInventoryFutureAllocationWriterV1, InventoryFutureAllocationWriter>();
        services.AddScoped<IInventoryFutureAllocationReadPortV1, InventoryFutureAllocationReadPort>();
        services.AddScoped<IInventoryFutureAllocationReleasePortV1, InventoryFutureAllocationReleasePort>();
        services.AddScoped<IInventoryFutureDemandSourceV1, InventoryFutureDemandSource>();
        services.TryAddScoped<IInventoryFutureUnitConverterV1, InventoryIdentityFutureUnitConverterV1>();
        services.TryAddScoped<IInventoryFutureSupplySourceV1, InventoryUnavailableFutureSupplySourceV1>();
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
