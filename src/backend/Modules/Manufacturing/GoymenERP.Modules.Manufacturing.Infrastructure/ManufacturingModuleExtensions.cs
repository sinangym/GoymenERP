using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Manufacturing.Infrastructure;

/// <summary>
/// Manufacturing's module registration (MFG-5). The API host does not call it yet: host wiring,
/// permissions and HTTP are a later slice, and the central writer's reviewed-inventory registration
/// of this DbContext and initializer comes with it. The audit interceptor is the host's to add
/// through <paramref name="configureDbContext"/>, as for every module.
/// </summary>
public static class ManufacturingModuleExtensions
{
    public static IServiceCollection AddManufacturingModule(
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

        services.AddDbContext<ManufacturingDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ManufacturingStore>();
        services.AddScoped<IManufacturingPlanStore>(serviceProvider => serviceProvider.GetRequiredService<ManufacturingStore>());
        services.AddScoped<IManufacturingOrderStore>(serviceProvider => serviceProvider.GetRequiredService<ManufacturingStore>());
        return services;
    }

    /// <summary>Applies Manufacturing's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeManufacturingModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ManufacturingDbContext>();

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
