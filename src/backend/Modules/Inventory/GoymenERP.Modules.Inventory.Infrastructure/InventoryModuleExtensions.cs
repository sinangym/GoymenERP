using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
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
        // INV-OPEN-1 part 2: the opening-count command; its location and product ports are the host's adapters.
        services.AddScoped<IInventoryOpeningCommandService, InventoryOpeningCommandService>();
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
